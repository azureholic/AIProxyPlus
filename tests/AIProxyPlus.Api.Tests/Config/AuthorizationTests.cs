using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using AIProxyPlus.Api.Tests.Infrastructure;
using AIProxyPlus.Domain.Entities;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;

namespace AIProxyPlus.Api.Tests.Config;

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

        var response = await client.GetAsync("/api/v1/config/models");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConfigRoute_WithWrongAdminKey_Returns401()
    {
        var client = ClientWithoutHeader("X-Admin-Api-Key");
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", "not-the-real-key");

        var response = await client.GetAsync("/api/v1/config/models");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConfigRoute_WithValidAdminKey_Succeeds()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/config/models");

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

        // 404 (unknown model) proves auth succeeded and the request reached routing, not 401.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task IngressRoute_WithEnrolledManagedIdentityBearer_PassesAuthAndReachesRouting()
    {
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        _factory.Consumers.Seed(Consumer.CreateManagedIdentity(
            Guid.NewGuid(), "managed-worker", tenantId, objectId));
        var client = ClientWithoutHeader("X-Api-Key");
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(
            ApiTestFactory.ManagedIdentityIssuer,
            [new("tid", tenantId.ToString()), new("oid", objectId.ToString()), new("roles", "AIProxyPlus.Invoke")]));

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions", new { model = "never-registered", messages = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task IngressRoute_WithEnrolledOidcBearer_PassesAuthAndReachesRouting()
    {
        _factory.Consumers.Seed(Consumer.CreateOidc(
            Guid.NewGuid(), "release-agent", ApiTestFactory.WorkloadOidcIssuer, "agent-42"));
        var client = ClientWithoutHeader("X-Api-Key");
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(
            ApiTestFactory.WorkloadOidcIssuer,
            [new("sub", "agent-42"), new("scope", "aiproxyplus.invoke")]));

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions", new { model = "never-registered", messages = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task IngressRoute_WithManagedIdentityMissingRole_Returns403()
    {
        var client = ClientWithoutHeader("X-Api-Key");
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(
            ApiTestFactory.ManagedIdentityIssuer,
            [new("tid", Guid.NewGuid().ToString()), new("oid", Guid.NewGuid().ToString())]));

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions", new { model = "x", messages = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IngressRoute_WithUnknownManagedIdentity_Returns401()
    {
        var client = ClientWithoutHeader("X-Api-Key");
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(
            ApiTestFactory.ManagedIdentityIssuer,
            [new("tid", Guid.NewGuid().ToString()), new("oid", Guid.NewGuid().ToString()), new("roles", "AIProxyPlus.Invoke")]));

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions", new { model = "x", messages = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IngressRoute_WithDisabledManagedIdentity_Returns403()
    {
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var consumer = Consumer.CreateManagedIdentity(Guid.NewGuid(), "disabled-worker", tenantId, objectId);
        consumer.Disable();
        _factory.Consumers.Seed(consumer);
        var client = ClientWithoutHeader("X-Api-Key");
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(
            ApiTestFactory.ManagedIdentityIssuer,
            [new("tid", tenantId.ToString()), new("oid", objectId.ToString()), new("roles", "AIProxyPlus.Invoke")]));

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions", new { model = "x", messages = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IngressRoute_WithOidcBearerMissingScope_Returns403()
    {
        var client = ClientWithoutHeader("X-Api-Key");
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(
            ApiTestFactory.WorkloadOidcIssuer,
            [new("sub", "agent-42")]));

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions", new { model = "x", messages = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IngressRoute_WithMalformedManagedIdentityClaims_Returns401()
    {
        var client = ClientWithoutHeader("X-Api-Key");
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(
            ApiTestFactory.ManagedIdentityIssuer,
            [new("tid", "not-a-guid"), new("oid", Guid.NewGuid().ToString()), new("roles", "AIProxyPlus.Invoke")]));

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions", new { model = "x", messages = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IngressRoute_WithExpiredBearer_Returns401()
    {
        var client = ClientWithoutHeader("X-Api-Key");
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(
            ApiTestFactory.ManagedIdentityIssuer,
            [new("tid", Guid.NewGuid().ToString()), new("oid", Guid.NewGuid().ToString()), new("roles", "AIProxyPlus.Invoke")],
            expires: DateTime.UtcNow.AddMinutes(-1)));

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions", new { model = "x", messages = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IngressRoute_WithBearerForWrongAudience_Returns401()
    {
        var client = ClientWithoutHeader("X-Api-Key");
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(
            ApiTestFactory.ManagedIdentityIssuer,
            [new("tid", Guid.NewGuid().ToString()), new("oid", Guid.NewGuid().ToString()), new("roles", "AIProxyPlus.Invoke")],
            "api://different-resource"));

        var response = await client.PostAsJsonAsync(
            "/openai/v1/chat/completions", new { model = "x", messages = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OpenApiDocument_IsGeneratedSuccessfully()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var schemes = body.GetProperty("components").GetProperty("securitySchemes");
        schemes.TryGetProperty("BrowserSession", out _).Should().BeTrue();
        schemes.TryGetProperty("AdminApiKey", out _).Should().BeTrue();
        schemes.TryGetProperty("ConsumerApiKey", out _).Should().BeTrue();
        schemes.TryGetProperty("WorkloadBearer", out _).Should().BeTrue();
        schemes.TryGetProperty("RealtimeTicket", out _).Should().BeTrue();

        var configSecurity = body.GetProperty("paths")
            .GetProperty("/api/v1/config/consumers")
            .GetProperty("get")
            .GetProperty("security");
        configSecurity.EnumerateArray().Any(item => item.TryGetProperty("BrowserSession", out _)).Should().BeTrue();
        configSecurity.EnumerateArray().Any(item => item.TryGetProperty("AdminApiKey", out _)).Should().BeTrue();
    }

    private static string CreateToken(
        string issuer,
        IEnumerable<Claim> claims,
        string audience = ApiTestFactory.WorkloadAudience,
        DateTime? expires = null)
    {
        var credentials = new SigningCredentials(ApiTestFactory.WorkloadSigningKey, SecurityAlgorithms.HmacSha256);
        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(5);
        var notBefore = expiresAt <= DateTime.UtcNow
            ? expiresAt.AddMinutes(-5)
            : DateTime.UtcNow.AddMinutes(-1);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore,
            expiresAt,
            credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
