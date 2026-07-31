using System.Net;
using System.Net.Http.Json;
using AiProxy.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AiProxy.Api.Tests.Config;

public class AuthorizationTests : IDisposable
{
    private readonly ApiTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient ClientWithoutHeader(string headerName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Remove(headerName);
        return client;
    }

    [Fact]
    public async Task ConfigRoute_WithoutAdminKey_Returns401()
    {
        var client = ClientWithoutHeader("X-Admin-Api-Key");

        var response = await client.GetAsync("/api/v1/config/model-deployments");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConfigRoute_WithWrongAdminKey_Returns401()
    {
        var client = ClientWithoutHeader("X-Admin-Api-Key");
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", "not-the-real-key");

        var response = await client.GetAsync("/api/v1/config/model-deployments");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConfigRoute_WithValidAdminKey_Succeeds()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/config/model-deployments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UsageRoute_WithoutAdminKey_Returns401()
    {
        var client = ClientWithoutHeader("X-Admin-Api-Key");

        var response = await client.GetAsync("/api/v1/usage");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IngressRoute_WithoutApiKey_Returns401()
    {
        var client = ClientWithoutHeader("X-Api-Key");

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions", new { model = "x", messages = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IngressRoute_WithUnknownApiKey_Returns401()
    {
        var client = ClientWithoutHeader("X-Api-Key");
        client.DefaultRequestHeaders.Add("X-Api-Key", "not-a-real-key");

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions", new { model = "x", messages = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IngressRoute_WithValidApiKey_PassesAuthAndReachesRouting()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions", new { model = "never-registered", messages = Array.Empty<object>() });

        // 404 (unknown deployment) proves auth succeeded and the request reached routing, not 401.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OpenApiDocument_IsGeneratedSuccessfully()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("openapi");
    }
}
