using System.Net;
using System.Net.Http.Json;
using AiProxy.Api.Tests.Infrastructure;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AiProxy.Api.Tests.Routing;

public class LoadBalancingTests : IAsyncLifetime, IDisposable
{
    private readonly ApiTestFactory _factory = new();
    private WireMockServer _backend1 = null!;
    private WireMockServer _backend2 = null!;

    public Task InitializeAsync()
    {
        _backend1 = WireMockServer.Start();
        _backend2 = WireMockServer.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _backend1.Stop();
        _backend2.Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => _factory.Dispose();

    private static Domain.Entities.Endpoint NewApiKeyEndpoint(Guid deploymentId, string baseUrl, int priority, int maxRetries = 0) =>
        Domain.Entities.Endpoint.Create(
            Guid.NewGuid(), deploymentId, baseUrl, ApiFamily.OpenAI, priority, AuthMode.ApiKey,
            secretRef: "endpoints/x", maxRetries: maxRetries);

    private static object ChatBody(string model) => new { model, messages = new[] { new { role = "user", content = "hi" } } };

    [Fact]
    public async Task SamePriorityEndpoints_SplitTrafficAcrossRepeatedCalls()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 1));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-1" }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2" }));

        var client = _factory.CreateClient();
        for (var i = 0; i < 8; i++)
        {
            var response = await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Round-robin across two equal-priority endpoints over 8 requests must hit both, not just one.
        _backend1.LogEntries.Should().NotBeEmpty();
        _backend2.LogEntries.Should().NotBeEmpty();
        (_backend1.LogEntries.Count() + _backend2.LogEntries.Count()).Should().Be(8);
    }

    [Fact]
    public async Task HigherPriorityEndpoint_IsAlwaysPreferredOverLowerPriority_DespiteRoundRobinState()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-1" }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2" }));

        var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));
        }

        _backend1.LogEntries.Should().HaveCount(5);
        _backend2.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Backend429_TriggersFailoverToNextPriorityEndpoint_SameAs500()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "1"));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2" }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("resp-2");
    }

    [Fact]
    public async Task AllEndpointsReturn429_Returns503()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var deployment = ModelDeployment.Create(Guid.NewGuid(), deploymentName);
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend1.Url!, priority: 1));
        deployment.AddEndpoint(NewApiKeyEndpoint(deployment.Id, _backend2.Url!, priority: 2));
        _factory.Deployments.Seed(deployment);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(deploymentName));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
