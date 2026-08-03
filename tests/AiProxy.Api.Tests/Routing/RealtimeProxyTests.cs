using System.Net.WebSockets;
using System.Text;
using AiProxy.Application.Abstractions;
using AiProxy.Api.Tests.Infrastructure;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;

namespace AiProxy.Api.Tests.Routing;

/// <summary>
/// End-to-end tests for the Realtime (voice) WebSocket proxy (architecture.md §9): connection-scoped
/// selection, handshake-only failover, and auth/deployment resolution failing fast before any upgrade.
/// </summary>
public class RealtimeProxyTests : IAsyncLifetime, IDisposable
{
    private readonly ApiTestFactory _factory = new();
    private FakeRealtimeBackend _backend1 = null!;
    private FakeRealtimeBackend _backend2 = null!;

    public async Task InitializeAsync()
    {
        _backend1 = await FakeRealtimeBackend.StartAsync();
        _backend2 = await FakeRealtimeBackend.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _backend1.DisposeAsync();
        await _backend2.DisposeAsync();
    }

    public void Dispose() => _factory.Dispose();

    private static ModelDeploymentEndpoint NewApiKeyEndpoint(Guid deploymentId, string baseUrl, int priority)
    {
        var endpointId = Guid.NewGuid();
        var endpoint = Endpoint.Create(
            endpointId, $"realtime-test-endpoint-{endpointId:N}", baseUrl, ApiFamily.OpenAI, AuthMode.ApiKey,
            secretRef: "endpoints/x");
        return ModelDeploymentEndpoint.Create(deploymentId, endpoint, priority, enabled: true);
    }

    private WebSocketClient NewAuthenticatedWebSocketClient()
    {
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["X-Api-Key"] = ApiTestFactory.DefaultConsumerApiKey;
        return client;
    }

    private Uri RealtimeUri(string model) => new(_factory.Server.BaseAddress, $"/openai/v1/realtime?model={model}");

    [Fact]
    public async Task SuccessfulHandshake_EchoesMessagesOverTheConnection()
    {
        var deploymentName = $"realtime-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url, priority: 1));
        _factory.Deployments.Seed(deployment);

        using var socket = await NewAuthenticatedWebSocketClient().ConnectAsync(RealtimeUri(deploymentName), CancellationToken.None);

        var payload = Encoding.UTF8.GetBytes("hello realtime");
        await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        var buffer = new byte[1024];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);

        Encoding.UTF8.GetString(buffer, 0, result.Count).Should().Be("hello realtime");
        _backend1.AcceptedConnectionCount.Should().Be(1);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    [Fact]
    public async Task SuccessfulHandshake_AuthenticatesViaApiKeyQueryParam_WhenNoHeaderIsSet()
    {
        // Browsers' native WebSocket API can't set custom headers, so the frontend's Voice Playground
        // (architecture.md §6.5) authenticates this one route via ?api_key= instead of X-Api-Key.
        var deploymentName = $"realtime-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url, priority: 1));
        _factory.Deployments.Seed(deployment);

        var uri = new Uri(_factory.Server.BaseAddress, $"/openai/v1/realtime?model={deploymentName}&api_key={ApiTestFactory.DefaultConsumerApiKey}");
        using var socket = await _factory.Server.CreateWebSocketClient().ConnectAsync(uri, CancellationToken.None);

        _backend1.AcceptedConnectionCount.Should().Be(1);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    [Fact]
    public async Task TicketHandshake_ConsumesTicketOnce_AndRejectsReplay()
    {
        var deploymentName = $"realtime-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url, priority: 1));
        _factory.Deployments.Seed(deployment);
        var consumer = Consumer.CreateOidc(
            Guid.NewGuid(),
            "proxy-admin",
            "https://identity.example.com/realms/ai-proxy",
            "subject-1");
        _factory.Consumers.Seed(consumer);
        var ticket = await _factory.RealtimeTickets.IssueAsync(
            consumer,
            RealtimeTicketRoutes.OpenAiRealtime,
            TimeSpan.FromSeconds(30));
        var uri = new Uri(_factory.Server.BaseAddress, $"/openai/v1/realtime?model={deploymentName}&ticket={ticket}");

        using (var socket = await _factory.Server.CreateWebSocketClient().ConnectAsync(uri, CancellationToken.None))
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }

        var replay = async () => await _factory.Server.CreateWebSocketClient().ConnectAsync(uri, CancellationToken.None);
        await replay.Should().ThrowAsync<InvalidOperationException>().WithMessage("*401*");
    }

    [Fact]
    public async Task TicketHandshake_WhenConsumerDisabled_IsRejectedBeforeUpgrade()
    {
        var consumer = Consumer.CreateOidc(
            Guid.NewGuid(),
            "disabled-admin",
            "https://identity.example.com/realms/ai-proxy",
            "subject-disabled");
        var ticket = await _factory.RealtimeTickets.IssueAsync(
            consumer,
            RealtimeTicketRoutes.OpenAiRealtime,
            TimeSpan.FromSeconds(30));
        consumer.Revoke();
        var uri = new Uri(_factory.Server.BaseAddress, $"/openai/v1/realtime?model=unused&ticket={ticket}");

        var connect = async () => await _factory.Server.CreateWebSocketClient().ConnectAsync(uri, CancellationToken.None);

        await connect.Should().ThrowAsync<InvalidOperationException>().WithMessage("*401*");
    }

    [Fact]
    public async Task MissingModelQueryParameter_FailsBeforeUpgrade_With404()
    {
        var act = async () => await NewAuthenticatedWebSocketClient().ConnectAsync(
            new Uri(_factory.Server.BaseAddress, "/openai/v1/realtime"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*404*");
    }

    [Fact]
    public async Task UnknownDeployment_FailsBeforeUpgrade_With404()
    {
        var act = async () => await NewAuthenticatedWebSocketClient().ConnectAsync(
            RealtimeUri($"does-not-exist-{Guid.NewGuid():N}"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*404*");
    }

    [Fact]
    public async Task MissingApiKey_FailsBeforeUpgrade_With401()
    {
        var deploymentName = $"realtime-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url, priority: 1));
        _factory.Deployments.Seed(deployment);

        // Deliberately using the un-authenticated client (no ConfigureRequest / X-Api-Key header).
        var act = async () => await _factory.Server.CreateWebSocketClient().ConnectAsync(RealtimeUri(deploymentName), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*401*");
    }

    [Fact]
    public async Task HandshakeFailsOnFirstPriorityEndpoint_FailsOverToSecondEndpoint()
    {
        _backend1.RejectHandshake = true;

        var deploymentName = $"realtime-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url, priority: 2));
        _factory.Deployments.Seed(deployment);

        using var socket = await NewAuthenticatedWebSocketClient().ConnectAsync(RealtimeUri(deploymentName), CancellationToken.None);

        _backend1.AcceptedConnectionCount.Should().Be(0);
        _backend2.AcceptedConnectionCount.Should().Be(1);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    [Fact]
    public async Task AllEndpointsFailHandshake_Returns503()
    {
        _backend1.RejectHandshake = true;
        _backend2.RejectHandshake = true;

        var deploymentName = $"realtime-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url, priority: 2));
        _factory.Deployments.Seed(deployment);

        var act = async () => await NewAuthenticatedWebSocketClient().ConnectAsync(RealtimeUri(deploymentName), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*503*");
    }
}
