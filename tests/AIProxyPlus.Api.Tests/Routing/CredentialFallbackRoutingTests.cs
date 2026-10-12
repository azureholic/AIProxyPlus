using System.Net;
using System.Net.Http.Json;
using AIProxyPlus.Api.Tests.Infrastructure;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AIProxyPlus.Api.Tests.Routing;

public class CredentialFallbackRoutingTests : IAsyncLifetime, IDisposable
{
    private readonly ApiTestFactory _factory = new();
    private WireMockServer _backend = null!;

    public Task InitializeAsync()
    {
        _backend = WireMockServer.Start();
        _factory.Credentials.Candidates = new EndpointCredentialCandidates(
            new EndpointCredential("Authorization", "Bearer active-key"),
            _ => Task.FromResult(new EndpointCredential("Authorization", "Bearer fallback-key")),
            EndpointCredentialSlot.Secondary,
            EndpointCredentialSlot.Primary);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _backend.Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task ActiveCredentialAccepted_DoesNotTryFallback()
    {
        var modelName = SeedModel();
        StubCredential("Bearer active-key", HttpStatusCode.OK, "active-response");
        StubCredential("Bearer fallback-key", HttpStatusCode.OK, "fallback-response");

        var response = await SendChatAsync(modelName);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("active-response");
        _backend.LogEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task ActiveCredentialRejectedWith401_TriesFallbackOnce()
    {
        var modelName = SeedModel();
        StubCredential("Bearer active-key", HttpStatusCode.Unauthorized, "active-rejected");
        StubCredential("Bearer fallback-key", HttpStatusCode.OK, "fallback-response");

        var response = await SendChatAsync(modelName);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("fallback-response");
        _backend.LogEntries.Should().HaveCount(2);
    }

    [Fact]
    public async Task BothCredentialsRejected_ReturnsOriginalActiveCredentialResponse()
    {
        var modelName = SeedModel();
        StubCredential("Bearer active-key", HttpStatusCode.Unauthorized, "active-rejected");
        StubCredential("Bearer fallback-key", HttpStatusCode.Unauthorized, "fallback-rejected");

        var response = await SendChatAsync(modelName);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("active-rejected");
        _backend.LogEntries.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task FallbackFailsWithPossiblyProcessedError_ReturnsFallbackErrorNotTheEarlier401(HttpStatusCode statusCode)
    {
        // The fallback request may have been processed before failing, so replaying the active-key 401 would hide that
        // and invite a duplicate retry. The caller sees the fallback's own error instead.
        var modelName = SeedModel();
        StubCredential("Bearer active-key", HttpStatusCode.Unauthorized, "active-rejected");
        StubCredential("Bearer fallback-key", statusCode, "fallback-failed");

        var response = await SendChatAsync(modelName);

        response.StatusCode.Should().Be(statusCode);
        (await response.Content.ReadAsStringAsync()).Should().Contain("fallback-failed");
        _backend.LogEntries.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FallbackRejectedBeforeProcessing_ReturnsTheOriginalActiveCredentialResponse(HttpStatusCode statusCode)
    {
        var modelName = SeedModel();
        StubCredential("Bearer active-key", HttpStatusCode.Unauthorized, "active-rejected");
        StubCredential("Bearer fallback-key", statusCode, "fallback-unavailable");

        var response = await SendChatAsync(modelName);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("active-rejected");
        _backend.LogEntries.Should().HaveCount(2);
    }
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task NonCredentialRejection_DoesNotTryFallback(HttpStatusCode statusCode)
    {
        var modelName = SeedModel();
        StubCredential("Bearer active-key", statusCode, "active-failure");
        StubCredential("Bearer fallback-key", HttpStatusCode.OK, "fallback-response");

        var response = await SendChatAsync(modelName);

        response.StatusCode.Should().Be(statusCode);
        _backend.LogEntries.Should().ContainSingle();
    }

    private string SeedModel()
    {
        var modelName = $"credential-fallback-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), $"endpoint-{Guid.NewGuid():N}", _backend.Url!, AuthMode.ApiKey,
            secretRef: "endpoints/primary", maxRetries: 0);
        var route = ApiRoute.Create(Guid.NewGuid(), "Native OpenAI", Provider.OpenAI, "/openai/v1");
        model.AddEndpoint(ModelEndpoint.Create(Guid.NewGuid(), model.Id, endpoint, route, 1, enabled: true));
        _factory.Models.Seed(model);
        return modelName;
    }

    private void StubCredential(string authorization, HttpStatusCode statusCode, string responseId)
    {
        _backend
            .Given(Request.Create()
                .WithPath("/openai/v1/chat/completions")
                .UsingPost()
                .WithHeader("Authorization", authorization))
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithBodyAsJson(new { id = responseId, choices = Array.Empty<object>() }));
    }

    private Task<HttpResponseMessage> SendChatAsync(string modelName) =>
        _factory.CreateClient().PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new { model = modelName, messages = new[] { new { role = "user", content = "hi" } } });
}