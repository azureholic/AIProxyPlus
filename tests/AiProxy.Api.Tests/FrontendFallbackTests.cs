using System.Net;
using AiProxy.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AiProxy.Api.Tests;

/// <summary>
/// The frontend SPA fallback (Program.cs, architecture.md §19) must serve "/" and any unknown client-side
/// route with a 200 (either the built app or the "not built yet" hint) without ever shadowing a real API
/// route — regression coverage for exactly that concern.
/// </summary>
public class FrontendFallbackTests : IDisposable
{
    private readonly ApiTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Root_ReturnsOk_WhetherOrNotTheFrontendHasBeenBuilt()
    {
        var response = await _factory.CreateClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnknownClientSideRoute_FallsBackToOk_NotNotFound()
    {
        var response = await _factory.CreateClient().GetAsync("/playground/chat");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Fallback_DoesNotShadowKnownApiAndIngressRoutes()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Missing X-Api-Key/X-Admin-Api-Key must still 401 — not fall through to the SPA/200 fallback.
        var noAuthClient = _factory.CreateClient();
        noAuthClient.DefaultRequestHeaders.Remove("X-Api-Key");
        noAuthClient.DefaultRequestHeaders.Remove("X-Admin-Api-Key");

        (await noAuthClient.PostAsync("/openai/v1/chat/completions", content: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await noAuthClient.GetAsync("/api/v1/config/models")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
