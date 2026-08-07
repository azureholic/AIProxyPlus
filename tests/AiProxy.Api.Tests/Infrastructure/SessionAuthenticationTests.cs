using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AiProxy.Api.Tests.Infrastructure;

public class SessionAuthenticationTests
{
    [Fact]
    public async Task Provider_WithoutAuthentication_ReturnsConfiguredDisplayName()
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/provider");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("displayName").GetString().Should().Be("OpenID Connect");
    }

    [Fact]
    public void SessionCookie_UsesAbsoluteExpiration()
    {
        using var factory = new ApiTestFactory();
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        options.SlidingExpiration.Should().BeFalse();
    }

    [Fact]
    public async Task Session_WithOidcCookie_ReturnsMinimalIdentity()
    {
        await using var factory = new ApiTestFactory();
        using var client = CreateSessionClient(factory);

        var response = await client.GetAsync("/api/v1/auth/session");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("username").GetString().Should().Be("proxy-admin");
        payload.GetProperty("issuer").GetString().Should().Be("https://identity.example.com/realms/ai-proxy");
    }

    [Fact]
    public async Task Inference_WithOidcCookie_ProvisionsConsumerBeforeRouting()
    {
        await using var factory = new ApiTestFactory();
        using var client = CreateSessionClient(factory);
        await AddAntiforgeryTokenAsync(client);

        var response = await client.PostAsJsonAsync("/openai/v1/chat/completions", new { model = "missing" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var consumer = await factory.Consumers.GetByOidcIdentityAsync(
            "https://identity.example.com/realms/ai-proxy",
            "subject-1");
        consumer.Should().NotBeNull();
        consumer!.Name.Should().Be("proxy-admin");
    }

    [Fact]
    public async Task AdminGet_WithOidcCookie_IsAuthorized()
    {
        await using var factory = new ApiTestFactory();
        using var client = CreateSessionClient(factory);

        var response = await client.GetAsync("/api/v1/config/consumers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnsafeAdminRequest_WithOidcCookieAndNoAntiforgeryToken_IsRejected()
    {
        await using var factory = new ApiTestFactory();
        using var client = CreateSessionClient(factory);

        var response = await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = "new-consumer" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RealtimeTicket_WithOidcSessionAndAntiforgery_ReturnsOpaqueTicket()
    {
        await using var factory = new ApiTestFactory();
        using var client = CreateSessionClient(factory);
        await AddAntiforgeryTokenAsync(client);

        var response = await client.PostAsync("/api/v1/auth/realtime-ticket", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("ticket").GetString().Should().NotBeNullOrWhiteSpace();
        payload.GetProperty("expiresInSeconds").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task RealtimeTicket_FromCrossOrigin_IsRejected()
    {
        await using var factory = new ApiTestFactory();
        using var client = CreateSessionClient(factory);
        await AddAntiforgeryTokenAsync(client);
        client.DefaultRequestHeaders.Add("Origin", "https://malicious.example.com");

        var response = await client.PostAsync("/api/v1/auth/realtime-ticket", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_FromCrossSiteNavigation_IsRejected()
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");

        var response = await client.GetAsync("/api/v1/auth/login");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RealtimeHandshake_WithOidcSessionButNoTicket_IsRejected()
    {
        await using var factory = new ApiTestFactory();
        using var client = CreateSessionClient(factory);

        var response = await client.GetAsync("/openai/v1/realtime?model=unused");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RealtimeHandshake_FromCrossOrigin_IsRejected()
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://malicious.example.com");

        var response = await client.GetAsync("/openai/v1/realtime?model=unused&api_key=test-consumer-key");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static HttpClient CreateSessionClient(ApiTestFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove("X-Api-Key");
        client.DefaultRequestHeaders.Remove("X-Admin-Api-Key");

        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var identity = new ClaimsIdentity(
            [
                new Claim("iss", "https://identity.example.com/realms/ai-proxy"),
                new Claim("sub", "subject-1"),
                new Claim("preferred_username", "proxy-admin"),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            "preferred_username",
            ClaimTypes.Role);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            CookieAuthenticationDefaults.AuthenticationScheme);
        var protectedTicket = options.TicketDataFormat.Protect(ticket);
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={protectedTicket}");
        return client;
    }

    private static async Task AddAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/auth/antiforgery");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", payload.GetProperty("token").GetString());
    }
}