using System.Net.WebSockets;
using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Domain.Services;
using AiProxy.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;

namespace AiProxy.Infrastructure.Routing;

/// <summary>
/// Proxies the OpenAI Realtime (voice) API — a WebSocket, not a request/response call (architecture.md §9).
/// The connection itself <em>is</em> the affinity: one endpoint is chosen once, for the lifetime of the
/// socket, using the same priority + round-robin selection as HTTP ingress. Failover only applies to the
/// backend handshake (before any client data has flowed) — a connection that drops mid-call is a
/// client-visible error, never silently retried on a different endpoint.
/// </summary>
public sealed class RealtimeProxyService(
    IModelRepository modelRepository,
    IEndpointHealthTracker endpointHealthTracker,
    IEndpointRoundRobinCounter roundRobinCounter,
    ICredentialProvider credentialProvider,
    UpstreamRouteResolver upstreamRouteResolver)
{
    public async Task<RoutingFailure?> ProxyAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        using var rootActivity = AiProxyActivitySource.Instance.StartActivity("ai_proxy.request");
        rootActivity?.SetTag("operation_type", OperationType.RealtimeVoice.ToString());

        var modelName = httpContext.Request.Query["model"].ToString();
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return new RoutingFailure(RoutingFailureReason.ModelNotFound, "Request must include a 'model' query parameter.");
        }

        Model? model;
        using (AiProxyActivitySource.Instance.StartActivity("model_resolution"))
        {
            model = await modelRepository.GetByNameAsync(modelName, cancellationToken);
        }

        if (model is null)
        {
            return new RoutingFailure(RoutingFailureReason.ModelNotFound, $"Model '{modelName}' was not found.");
        }

        rootActivity?.SetTag("model", model.Name);

        var roundRobinSeed = roundRobinCounter.Next(model.Id);
        var excludedEndpointIds = new HashSet<Guid>();

        while (true)
        {
            var selection = EndpointSelector.SelectEndpoint(model.Endpoints, excludedEndpointIds: excludedEndpointIds, roundRobinCounter: roundRobinSeed);
            if (!selection.IsSuccess)
            {
                return new RoutingFailure(RoutingFailureReason.NoHealthyEndpoint, selection.FailureReason!);
            }

            var assignment = selection.SelectedEndpoint!;
            var endpoint = assignment.Endpoint;

            Uri targetUri;
            try
            {
                targetUri = upstreamRouteResolver.ResolveRealtime(assignment, httpContext.Request.QueryString.Value ?? string.Empty);
            }
            catch (NotSupportedException)
            {
                excludedEndpointIds.Add(endpoint.Id);
                continue;
            }

            EndpointCredential credential;
            using (AiProxyActivitySource.Instance.StartActivity("secret_resolution"))
            {
                credential = await credentialProvider.GetCredentialAsync(assignment, cancellationToken);
            }

            var backendSocket = new ClientWebSocket();
            backendSocket.Options.SetRequestHeader(credential.HeaderName, credential.HeaderValue);

            using (AiProxyActivitySource.Instance.StartActivity("backend_call")?.SetTag("endpoint", endpoint.BaseUrl))
            {
                try
                {
                    await backendSocket.ConnectAsync(targetUri, cancellationToken);
                }
                catch (Exception ex) when (ex is WebSocketException or HttpRequestException)
                {
                    backendSocket.Dispose();
                    endpointHealthTracker.MarkUnhealthy(endpoint.Id);
                    excludedEndpointIds.Add(endpoint.Id);
                    continue;
                }
            }

            endpointHealthTracker.MarkHealthy(endpoint.Id);

            using (backendSocket)
            using (var clientSocket = await httpContext.WebSockets.AcceptWebSocketAsync())
            {
                await PumpBidirectionallyAsync(clientSocket, backendSocket, cancellationToken);
            }

            return null;
        }
    }

    private static async Task PumpBidirectionallyAsync(WebSocket client, WebSocket backend, CancellationToken cancellationToken)
    {
        var clientToBackend = PumpAsync(client, backend, cancellationToken);
        var backendToClient = PumpAsync(backend, client, cancellationToken);
        await Task.WhenAny(clientToBackend, backendToClient);
    }

    private static async Task PumpAsync(WebSocket source, WebSocket destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (source.State == WebSocketState.Open && destination.State == WebSocketState.Open)
            {
                var result = await source.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // Ack the close back to whichever side sent it (required for that side's own CloseAsync
                    // call to complete instead of hanging/erroring), and propagate the closing intent to the
                    // other side of the relay so neither leg is left dangling.
                    if (source.State == WebSocketState.CloseReceived)
                    {
                        await source.CloseAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription, cancellationToken);
                    }

                    if (destination.State == WebSocketState.Open)
                    {
                        await destination.CloseAsync(
                            result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription, cancellationToken);
                    }

                    return;
                }

                await destination.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, cancellationToken);
            }
        }
        catch (WebSocketException)
        {
            // The other side disconnecting mid-pump ends this leg of the relay; the sibling pump task (or the
            // caller once both complete) is responsible for the overall connection's lifecycle, not this leg.
        }
    }
}
