using System.Net;
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
/// selection, handshake-only failover, and auth/model resolution failing fast before any upgrade.
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

    private static ModelEndpoint NewApiKeyEndpoint(Guid modelId, string baseUrl, int priority)
    {
        var endpointId = Guid.NewGuid();
        var endpoint = Endpoint.Create(
            endpointId, $"realtime-test-endpoint-{endpointId:N}", baseUrl, AuthMode.ApiKey,
            secretRef: "endpoints/x");
        var route = ApiRoute.Create(Guid.NewGuid(), "Native OpenAI", Provider.OpenAI, "/openai/v1");
        return ModelEndpoint.Create(Guid.NewGuid(), modelId, endpoint, route, priority, enabled: true);
    }

    private WebSocketClient NewAuthenticatedWebSocketClient()
    {
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["X-Api-Key"] = ApiTestFactory.DefaultConsumerApiKey;
        return client;
    }

    private Uri RealtimeUri(string model) => new(_factory.Server.BaseAddress, $"/openai/v1/realtime?model={model}");

    private async Task<IReadOnlyList<UsageRecord>> WaitForUsageRecordCountAsync(int expectedCount)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var records = _factory.UsageRecords.Records;
            if (records.Count == expectedCount)
            {
                return records;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Expected {expectedCount} Realtime usage records within the timeout.");
    }

    [Fact]
    public async Task SuccessfulHandshake_EchoesMessagesOverTheConnection()
    {
        var modelName = $"realtime-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url, priority: 1));
        _factory.Models.Seed(model);

        using var socket = await NewAuthenticatedWebSocketClient().ConnectAsync(RealtimeUri(modelName), CancellationToken.None);

        var payload = Encoding.UTF8.GetBytes("hello realtime");
        await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        var buffer = new byte[1024];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);

        Encoding.UTF8.GetString(buffer, 0, result.Count).Should().Be("hello realtime");
        _backend1.AcceptedConnectionCount.Should().Be(1);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    [Fact]
    public async Task FragmentedResponseDone_ForwardsUnchangedAndCapturesAttributedUsage()
    {
        const string responseId = "resp_realtime_usage_1";
        const string firstFragment = "{\"type\":\"response.done\",\"response\":{\"id\":\"resp_realtime";
        const string secondFragment = "_usage_1\",\"usage\":{\"input_tokens\":12,\"output_tokens\":8,\"total_tokens\":20}}}";
        _backend1.ServerMessageFragments = [firstFragment, secondFragment];
        var modelName = $"realtime-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        var assignment = NewApiKeyEndpoint(model.Id, _backend1.Url, priority: 1);
        model.AddEndpoint(assignment);
        _factory.Models.Seed(model);

        using var socket = await NewAuthenticatedWebSocketClient().ConnectAsync(RealtimeUri(modelName), CancellationToken.None);

        var buffer = new byte[1024];
        var firstResult = await socket.ReceiveAsync(buffer, CancellationToken.None);
        Encoding.UTF8.GetString(buffer, 0, firstResult.Count).Should().Be(firstFragment);
        firstResult.MessageType.Should().Be(WebSocketMessageType.Text);
        firstResult.EndOfMessage.Should().BeFalse();
        var secondResult = await socket.ReceiveAsync(buffer, CancellationToken.None);
        Encoding.UTF8.GetString(buffer, 0, secondResult.Count).Should().Be(secondFragment);
        secondResult.MessageType.Should().Be(WebSocketMessageType.Text);
        secondResult.EndOfMessage.Should().BeTrue();
        var usage = (await WaitForUsageRecordCountAsync(1)).Single();
        usage.Should().BeEquivalentTo(new
        {
            ModelName = modelName,
            ConsumerId = _factory.DefaultConsumer.Id,
            ConsumerName = _factory.DefaultConsumer.Name,
            EndpointId = assignment.EndpointId,
            EndpointName = assignment.Endpoint.Name,
            Provider = Provider.OpenAI,
            OperationType = OperationType.RealtimeVoice,
            RequestId = responseId,
            PromptTokens = (int?)12,
            CompletionTokens = (int?)8,
            TotalTokens = (int?)20,
            Streaming = true,
            HttpStatusCode = (int)HttpStatusCode.SwitchingProtocols,
        });
        usage.SessionId.Should().NotBeNullOrWhiteSpace();

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    [Fact]
    public async Task MultipleResponseDoneEvents_CapturesEachResponseOnce_AndIgnoresInvalidEvents()
    {
        const string firstResponse = "{\"type\":\"response.done\",\"response\":{\"id\":\"resp_1\",\"usage\":{\"input_tokens\":4,\"output_tokens\":6,\"total_tokens\":10}}}";
        const string secondResponse = "{\"type\":\"response.done\",\"response\":{\"id\":\"resp_2\",\"usage\":{\"input_tokens\":7,\"output_tokens\":3,\"total_tokens\":10}}}";
        _backend1.ServerMessageFragments = [firstResponse];
        var modelName = $"realtime-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url, priority: 1));
        _factory.Models.Seed(model);

        using var socket = await NewAuthenticatedWebSocketClient().ConnectAsync(RealtimeUri(modelName), CancellationToken.None);
        var buffer = new byte[1024];
        await socket.ReceiveAsync(buffer, CancellationToken.None);

        foreach (var message in new[]
        {
            secondResponse,
            secondResponse,
            "{not-json",
            "{\"type\":1}",
            "{\"type\":\"response.done\",\"response\":{\"id\":\"resp_without_usage\"}}",
        })
        {
            var payload = Encoding.UTF8.GetBytes(message);
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            Encoding.UTF8.GetString(buffer, 0, result.Count).Should().Be(message);
        }

        var records = await WaitForUsageRecordCountAsync(2);
        records.Select(record => record.RequestId).Should().BeEquivalentTo("resp_1", "resp_2");
        records.Select(record => record.SessionId).Distinct().Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    [Fact]
    public async Task SuccessfulHandshake_AuthenticatesViaApiKeyQueryParam_WhenNoHeaderIsSet()
    {
        // Browsers' native WebSocket API can't set custom headers, so the frontend's Voice Playground
        // (architecture.md §6.5) authenticates this one route via ?api_key= instead of X-Api-Key.
        var modelName = $"realtime-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url, priority: 1));
        _factory.Models.Seed(model);

        var uri = new Uri(_factory.Server.BaseAddress, $"/openai/v1/realtime?model={modelName}&api_key={ApiTestFactory.DefaultConsumerApiKey}");
        using var socket = await _factory.Server.CreateWebSocketClient().ConnectAsync(uri, CancellationToken.None);

        _backend1.AcceptedConnectionCount.Should().Be(1);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    [Fact]
    public async Task TicketHandshake_ConsumesTicketOnce_AndRejectsReplay()
    {
        var modelName = $"realtime-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url, priority: 1));
        _factory.Models.Seed(model);
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
        var uri = new Uri(_factory.Server.BaseAddress, $"/openai/v1/realtime?model={modelName}&ticket={ticket}");

        using (var socket = await _factory.Server.CreateWebSocketClient().ConnectAsync(uri, CancellationToken.None))
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }

        var replay = async () => await _factory.Server.CreateWebSocketClient().ConnectAsync(uri, CancellationToken.None);
        await replay.Should().ThrowAsync<InvalidOperationException>().WithMessage("*401*");
    }

    [Fact]
    public async Task ConnectMethod_WithAuthentication_ReachesRealtimeModelResolution()
    {
        using var request = new HttpRequestMessage(HttpMethod.Connect, "/openai/v1/realtime?model=unused");

        using var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
    public async Task UnknownModel_FailsBeforeUpgrade_With404()
    {
        var act = async () => await NewAuthenticatedWebSocketClient().ConnectAsync(
            RealtimeUri($"does-not-exist-{Guid.NewGuid():N}"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*404*");
    }

    [Fact]
    public async Task MissingApiKey_FailsBeforeUpgrade_With401()
    {
        var modelName = $"realtime-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url, priority: 1));
        _factory.Models.Seed(model);

        // Deliberately using the un-authenticated client (no ConfigureRequest / X-Api-Key header).
        var act = async () => await _factory.Server.CreateWebSocketClient().ConnectAsync(RealtimeUri(modelName), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*401*");
    }

    [Fact]
    public async Task HandshakeFailsOnFirstPriorityEndpoint_FailsOverToSecondEndpoint()
    {
        _backend1.RejectHandshake = true;

        var modelName = $"realtime-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url, priority: 2));
        _factory.Models.Seed(model);

        using var socket = await NewAuthenticatedWebSocketClient().ConnectAsync(RealtimeUri(modelName), CancellationToken.None);

        _backend1.AcceptedConnectionCount.Should().Be(0);
        _backend2.AcceptedConnectionCount.Should().Be(1);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    [Fact]
    public async Task AllEndpointsFailHandshake_Returns503()
    {
        _backend1.RejectHandshake = true;
        _backend2.RejectHandshake = true;

        var modelName = $"realtime-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url, priority: 2));
        _factory.Models.Seed(model);

        var act = async () => await NewAuthenticatedWebSocketClient().ConnectAsync(RealtimeUri(modelName), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*503*");
    }
}
