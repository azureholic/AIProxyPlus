using System.Net.WebSockets;
using System.Diagnostics;
using System.Text.Json;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Domain.Services;
using AIProxyPlus.Infrastructure.Observability;
using AIProxyPlus.Infrastructure.Usage;
using DomainEndpoint = AIProxyPlus.Domain.Entities.Endpoint;

namespace AIProxyPlus.Infrastructure.Routing;

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
    UpstreamRouteResolver upstreamRouteResolver,
    UsageRecordChannel usageRecordChannel,
    AIProxyPlusMetrics metrics)
{
    public async Task<RoutingFailure?> ProxyAsync(
        Microsoft.AspNetCore.Http.HttpContext httpContext,
        CancellationToken cancellationToken,
        Guid? consumerId = null,
        IReadOnlySet<Guid>? allowedModelIds = null,
        string? consumerName = null)
    {
        using var rootActivity = AIProxyPlusActivitySource.Instance.StartActivity("aiproxyplus.request");
        rootActivity?.SetTag("operation_type", OperationType.RealtimeVoice.ToString());

        var modelName = httpContext.Request.Query["model"].ToString();
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return new RoutingFailure(RoutingFailureReason.ModelNotFound, "Request must include a 'model' query parameter.");
        }

        Model? model;
        using (AIProxyPlusActivitySource.Instance.StartActivity("model_resolution"))
        {
            model = await modelRepository.GetByNameAsync(modelName, cancellationToken);
        }

        if (model is null)
        {
            return new RoutingFailure(RoutingFailureReason.ModelNotFound, $"Model '{modelName}' was not found.");
        }

        if (allowedModelIds is not null && !allowedModelIds.Contains(model.Id))
        {
            return new RoutingFailure(
                RoutingFailureReason.ModelAccessDenied,
                $"Consumer '{consumerName}' is not allowed to use model '{model.Name}'.");
        }

        rootActivity?.SetTag("model", model.Name);

        var roundRobinSeed = roundRobinCounter.Next(model.Id);
        var excludedEndpointIds = new HashSet<Guid>();

        // Cross-request passive circuit breaker (architecture.md §9) — see ProxyRoutingService for the HTTP
        // ingress equivalent of this same seeding.
        foreach (var candidate in model.Endpoints)
        {
            if (endpointHealthTracker.GetHealth(candidate.EndpointId) == EndpointHealthState.Unhealthy)
            {
                excludedEndpointIds.Add(candidate.EndpointId);
            }
        }

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
            using (AIProxyPlusActivitySource.Instance.StartActivity("secret_resolution"))
            {
                credential = await credentialProvider.GetCredentialAsync(assignment, cancellationToken);
            }

            var backendSocket = new ClientWebSocket();
            backendSocket.Options.SetRequestHeader(credential.HeaderName, credential.HeaderValue);

            using (AIProxyPlusActivitySource.Instance.StartActivity("backend_call")?.SetTag("endpoint", endpoint.BaseUrl))
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
                var usageObserver = consumerId.HasValue && !string.IsNullOrWhiteSpace(consumerName)
                    ? new RealtimeUsageObserver(
                        usageRecordChannel,
                        model,
                        endpoint,
                        assignment.ApiRoute.Provider,
                        consumerId.Value,
                        consumerName,
                        httpContext.TraceIdentifier,
                        metrics)
                    : null;
                await PumpBidirectionallyAsync(clientSocket, backendSocket, cancellationToken, usageObserver);
            }

            return null;
        }
    }

    private static async Task PumpBidirectionallyAsync(
        WebSocket client,
        WebSocket backend,
        CancellationToken cancellationToken,
        RealtimeUsageObserver? usageObserver)
    {
        var clientToBackend = PumpAsync(client, backend, cancellationToken);
        var backendToClient = PumpAsync(backend, client, cancellationToken, usageObserver);
        await Task.WhenAny(clientToBackend, backendToClient);
    }

    private static async Task PumpAsync(
        WebSocket source,
        WebSocket destination,
        CancellationToken cancellationToken,
        RealtimeUsageObserver? usageObserver = null)
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
                usageObserver?.Observe(buffer.AsSpan(0, result.Count), result.MessageType, result.EndOfMessage);
            }
        }
        catch (WebSocketException)
        {
            // The other side disconnecting mid-pump ends this leg of the relay; the sibling pump task (or the
            // caller once both complete) is responsible for the overall connection's lifecycle, not this leg.
        }
    }

    private sealed class RealtimeUsageObserver(
        UsageRecordChannel usageRecordChannel,
        Model model,
        DomainEndpoint endpoint,
        Provider provider,
        Guid consumerId,
        string consumerName,
        string sessionId,
        AIProxyPlusMetrics metrics)
    {
        private const int MaxObservedMessageBytes = 1024 * 1024;
        private const int MaxResponseIdChars = 256;
        private const int MaxTrackedResponseIds = 1024;
        private readonly MemoryStream _message = new();
        private readonly HashSet<string> _recordedResponseIds = new(StringComparer.Ordinal);
        private readonly Queue<string> _recordedResponseIdOrder = new();
        private readonly Stopwatch _sessionDuration = Stopwatch.StartNew();
        private bool _discardCurrentMessage;

        public void Observe(ReadOnlySpan<byte> payload, WebSocketMessageType messageType, bool endOfMessage)
        {
            if (messageType != WebSocketMessageType.Text)
            {
                ResetMessage();
                return;
            }

            if (_discardCurrentMessage)
            {
                if (endOfMessage)
                {
                    ResetMessage();
                }

                return;
            }

            if (_message.Length + payload.Length > MaxObservedMessageBytes)
            {
                _message.SetLength(0);
                _discardCurrentMessage = !endOfMessage;
                return;
            }

            _message.Write(payload);
            if (!endOfMessage)
            {
                return;
            }

            try
            {
                RecordUsage(_message.GetBuffer().AsSpan(0, checked((int)_message.Length)));
            }
            catch (JsonException)
            {
                // Usage observation is best-effort and must never affect WebSocket passthrough.
            }
            finally
            {
                ResetMessage();
            }
        }

        private void ResetMessage()
        {
            _message.SetLength(0);
            _discardCurrentMessage = false;
        }

        private void RecordUsage(ReadOnlySpan<byte> utf8Json)
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String ||
                type.GetString() != "response.done" ||
                !root.TryGetProperty("response", out var response) ||
                response.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var responseId = response.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(responseId) || responseId.Length > MaxResponseIdChars ||
                !response.TryGetProperty("usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var promptTokens = TryGetInt(usage, "input_tokens");
            var completionTokens = TryGetInt(usage, "output_tokens");
            var totalTokens = TryGetInt(usage, "total_tokens");
            if (promptTokens is null && completionTokens is null && totalTokens is null)
            {
                return;
            }

            if (!TryMarkResponseRecorded(responseId))
            {
                return;
            }

            usageRecordChannel.Enqueue(UsageRecord.Create(
                Guid.NewGuid(),
                model.Name,
                consumerId,
                consumerName,
                endpoint.Id,
                endpoint.Name,
                provider,
                OperationType.RealtimeVoice,
                responseId,
                streaming: true,
                Microsoft.AspNetCore.Http.StatusCodes.Status101SwitchingProtocols,
                checked((int)Math.Min(_sessionDuration.ElapsedMilliseconds, int.MaxValue)),
                sessionId: sessionId,
                promptTokens: promptTokens,
                completionTokens: completionTokens,
                totalTokens: totalTokens));

            metrics.RecordTokens(model.Name, endpoint.BaseUrl, promptTokens, completionTokens, totalTokens);
        }

        private bool TryMarkResponseRecorded(string responseId)
        {
            if (!_recordedResponseIds.Add(responseId))
            {
                return false;
            }

            _recordedResponseIdOrder.Enqueue(responseId);
            if (_recordedResponseIdOrder.Count > MaxTrackedResponseIds)
            {
                _recordedResponseIds.Remove(_recordedResponseIdOrder.Dequeue());
            }

            return true;
        }

        private static int? TryGetInt(JsonElement parent, string propertyName) =>
            parent.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var result) &&
            result >= 0
                ? result
                : null;
    }
}
