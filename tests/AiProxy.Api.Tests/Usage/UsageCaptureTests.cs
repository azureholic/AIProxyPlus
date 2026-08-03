using System.Net;
using System.Net.Http.Json;
using AiProxy.Api.Tests.Infrastructure;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AiProxy.Api.Tests.Usage;

public class UsageCaptureTests : IAsyncLifetime, IDisposable
{
    private readonly ApiTestFactory _factory = new();
    private WireMockServer _backend = null!;

    public Task InitializeAsync()
    {
        _backend = WireMockServer.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _backend.Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => _factory.Dispose();

    private static ModelDeploymentEndpoint NewApiKeyEndpoint(Guid deploymentId, string baseUrl)
    {
        var endpointId = Guid.NewGuid();
        var endpoint = Endpoint.Create(
            endpointId, $"usage-capture-openai-test-endpoint-{endpointId:N}", baseUrl, ApiFamily.OpenAI, AuthMode.ApiKey,
            secretRef: "endpoints/x", maxRetries: 0);
        return ModelDeploymentEndpoint.Create(deploymentId, endpoint, priority: 1, enabled: true);
    }

    private static ModelDeploymentEndpoint NewAnthropicEndpoint(Guid deploymentId, string baseUrl)
    {
        var endpointId = Guid.NewGuid();
        var endpoint = Endpoint.Create(
            endpointId, $"usage-capture-anthropic-test-endpoint-{endpointId:N}", baseUrl, ApiFamily.Anthropic, AuthMode.ApiKey,
            secretRef: "endpoints/x", maxRetries: 0);
        return ModelDeploymentEndpoint.Create(deploymentId, endpoint, priority: 1, enabled: true);
    }

    private static HttpRequestMessage BuildRequest(string path, object body) =>
        new(HttpMethod.Post, path) { Content = JsonContent.Create(body) };

    private async Task<UsageRecord> WaitForUsageRecordAsync()
    {
        for (var i = 0; i < 100; i++)
        {
            var record = _factory.UsageRecords.Records.FirstOrDefault(r => r.ConsumerId == _factory.DefaultConsumer.Id);
            if (record is not null)
            {
                return record;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("No usage record was captured within the timeout.");
    }

    [Fact]
    public async Task NonStreamingChatCompletion_CapturesTokenUsage()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend.Url!));
        _factory.Deployments.Seed(deployment);

        _backend.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { id = "resp-1", usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 } }));

        var response = await _factory.CreateClient().SendAsync(
            BuildRequest("/openai/v1/chat/completions", new { model = deploymentName, messages = new[] { new { role = "user", content = "hi" } } }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var record = await WaitForUsageRecordAsync();
        record.PromptTokens.Should().Be(10);
        record.CompletionTokens.Should().Be(5);
        record.TotalTokens.Should().Be(15);
        record.ConsumerName.Should().Be(_factory.DefaultConsumer.Name);
        record.OperationType.Should().Be(OperationType.ChatCompletion);
        record.ModelDeploymentName.Should().Be(deploymentName);
        record.Streaming.Should().BeFalse();
    }

    [Fact]
    public async Task NonStreamingImageGeneration_CapturesUsageMetricsWithoutTokens()
    {
        var deploymentName = $"dall-e-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend.Url!));
        _factory.Deployments.Seed(deployment);

        _backend.Given(Request.Create().WithPath("/openai/v1/images/generations").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(
                new { data = new[] { new { url = "https://example/1.png", size = "1024x1024" } } }));

        var response = await _factory.CreateClient().SendAsync(
            BuildRequest("/openai/v1/images/generations", new { model = deploymentName, prompt = "a cat" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var record = await WaitForUsageRecordAsync();
        record.PromptTokens.Should().BeNull();
        record.CompletionTokens.Should().BeNull();
        record.TotalTokens.Should().BeNull();
        record.UsageMetrics.Should().NotBeNull();
        record.UsageMetrics!["image_count"].Should().Be("1");
        record.UsageMetrics!["size"].Should().Be("1024x1024");
        record.OperationType.Should().Be(OperationType.ImageGeneration);
    }

    [Fact]
    public async Task StreamingChatCompletion_WithClientRequestedUsage_ForwardsUsageChunkAndCapturesUsage()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend.Url!));
        _factory.Deployments.Seed(deployment);

        const string sse =
            "data: {\"id\":\"c1\",\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n\n" +
            "data: {\"id\":\"c1\",\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: {\"id\":\"c1\",\"choices\":[],\"usage\":{\"prompt_tokens\":7,\"completion_tokens\":2,\"total_tokens\":9}}\n\n" +
            "data: [DONE]\n\n";

        _backend.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "text/event-stream").WithBody(sse));

        var response = await _factory.CreateClient().SendAsync(BuildRequest(
            "/openai/v1/chat/completions",
            new
            {
                model = deploymentName,
                messages = new[] { new { role = "user", content = "hi" } },
                stream = true,
                stream_options = new { include_usage = true },
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"usage\"", "the client explicitly asked for usage, so the forced usage chunk must be forwarded");

        var record = await WaitForUsageRecordAsync();
        record.PromptTokens.Should().Be(7);
        record.CompletionTokens.Should().Be(2);
        record.Streaming.Should().BeTrue();
    }

    [Fact]
    public async Task StreamingChatCompletion_WithoutClientRequestedUsage_SuppressesUsageChunkButStillCapturesUsage()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend.Url!));
        _factory.Deployments.Seed(deployment);

        const string sse =
            "data: {\"id\":\"c1\",\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n\n" +
            "data: {\"id\":\"c1\",\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: {\"id\":\"c1\",\"choices\":[],\"usage\":{\"prompt_tokens\":7,\"completion_tokens\":2,\"total_tokens\":9}}\n\n" +
            "data: [DONE]\n\n";

        _backend.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "text/event-stream").WithBody(sse));

        // Client does NOT set stream_options.include_usage — the proxy forces it on the outbound call anyway,
        // but must not leak the resulting usage-only frame back to a client that never asked for it.
        var response = await _factory.CreateClient().SendAsync(BuildRequest(
            "/openai/v1/chat/completions",
            new { model = deploymentName, messages = new[] { new { role = "user", content = "hi" } }, stream = true }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("\"usage\"");
        body.Should().Contain("[DONE]");

        var record = await WaitForUsageRecordAsync();
        record.PromptTokens.Should().Be(7);
        record.CompletionTokens.Should().Be(2);
    }

    [Fact]
    public async Task StreamingAnthropicMessages_CapturesInputAndOutputTokensFromMessageEvents()
    {
        var deploymentName = $"claude-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewAnthropicEndpoint(deployment.Id, _backend.Url!));
        _factory.Deployments.Seed(deployment);

        const string sse =
            "event: message_start\n" +
            "data: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\",\"usage\":{\"input_tokens\":12,\"output_tokens\":0}}}\n\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"delta\":{\"text\":\"Hi\"}}\n\n" +
            "event: message_delta\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":6}}\n\n" +
            "event: message_stop\n" +
            "data: {\"type\":\"message_stop\"}\n\n";

        _backend.Given(Request.Create().WithPath("/anthropic/v1/messages").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "text/event-stream").WithBody(sse));

        var response = await _factory.CreateClient().SendAsync(BuildRequest(
            "/anthropic/v1/messages",
            new { model = deploymentName, messages = new[] { new { role = "user", content = "hi" } }, stream = true }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("message_start").And.Contain("message_stop");

        var record = await WaitForUsageRecordAsync();
        record.PromptTokens.Should().Be(12);
        record.CompletionTokens.Should().Be(6);
    }

    [Fact]
    public async Task ResponseWithNoUsageSignal_SucceedsWithoutCapturingUsageRecord()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend.Url!));
        _factory.Deployments.Seed(deployment);

        _backend.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-no-usage" }));

        var response = await _factory.CreateClient().SendAsync(BuildRequest(
            "/openai/v1/chat/completions", new { model = deploymentName, messages = new[] { new { role = "user", content = "hi" } } }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await Task.Delay(200);
        _factory.UsageRecords.Records.Should().NotContain(r => r.ConsumerId == _factory.DefaultConsumer.Id);
    }
}
