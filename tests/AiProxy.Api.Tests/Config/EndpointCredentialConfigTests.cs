using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AiProxy.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AiProxy.Api.Tests.Config;

public sealed class EndpointCredentialConfigTests : IDisposable
{
    private readonly ApiTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task RotationLifecycle_SetActivateStopAndRemove_ReturnsMetadataOnly()
    {
        var client = _factory.CreateClient();
        var endpointId = await CreateApiKeyEndpointAsync(client);

        var setResponse = await client.PutAsJsonAsync(
            $"/api/v1/config/endpoints/{endpointId}/secrets/Secondary",
            new { apiKeyPlaintext = "sk-secondary-private" });
        setResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertMetadataOnlyAsync(setResponse, "sk-secondary-private", hasSecondary: true, activeSlot: "Primary");

        var fallbackExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var activateResponse = await client.PutAsJsonAsync(
            $"/api/v1/config/endpoints/{endpointId}/credential-activation",
            new { activeSlot = "Secondary", fallbackExpiresAt });
        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var activated = JsonDocument.Parse(await activateResponse.Content.ReadAsStringAsync()))
        {
            activated.RootElement.GetProperty("activeCredentialSlot").GetString().Should().Be("Secondary");
            activated.RootElement.GetProperty("fallbackCredentialSlot").GetString().Should().Be("Primary");
        }

        (await client.DeleteAsync($"/api/v1/config/endpoints/{endpointId}/secrets/Secondary"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.DeleteAsync($"/api/v1/config/endpoints/{endpointId}/credential-fallback"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync(
            $"/api/v1/config/endpoints/{endpointId}/credential-activation",
            new { activeSlot = "Primary" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var removeResponse = await client.DeleteAsync(
            $"/api/v1/config/endpoints/{endpointId}/secrets/Secondary");
        removeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertMetadataOnlyAsync(removeResponse, "sk-secondary-private", hasSecondary: false, activeSlot: "Primary");
    }

    [Fact]
    public async Task ActivateCredential_WhenFallbackExceedsMaximum_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var endpointId = await CreateApiKeyEndpointAsync(client);
        await client.PutAsJsonAsync(
            $"/api/v1/config/endpoints/{endpointId}/secrets/Secondary",
            new { apiKeyPlaintext = "sk-secondary" });

        var response = await client.PutAsJsonAsync(
            $"/api/v1/config/endpoints/{endpointId}/credential-activation",
            new { activeSlot = "Secondary", fallbackExpiresAt = DateTimeOffset.UtcNow.AddHours(2) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetCredential_WithManagedIdentityEndpoint_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/config/endpoints",
            new
            {
                name = $"managed-{Guid.NewGuid():N}",
                baseUrl = "https://managed.example.com",
                authMode = "ManagedIdentity",
                managedIdentityScope = "https://cognitiveservices.azure.com/.default",
            });
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var endpointId = created.RootElement.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/config/endpoints/{endpointId}/secrets/Secondary",
            new { apiKeyPlaintext = "must-not-store" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _factory.Secrets.ContainsPlaintext("must-not-store").Should().BeFalse();
    }

    private static async Task<Guid> CreateApiKeyEndpointAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/config/endpoints",
            new
            {
                name = $"rotation-{Guid.NewGuid():N}",
                baseUrl = "https://backend.example.com",
                authMode = "ApiKey",
                apiKeyPlaintext = "sk-primary",
            });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task AssertMetadataOnlyAsync(
        HttpResponseMessage response,
        string plaintext,
        bool hasSecondary,
        string activeSlot)
    {
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(plaintext);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("hasSecondarySecret").GetBoolean().Should().Be(hasSecondary);
        document.RootElement.GetProperty("activeCredentialSlot").GetString().Should().Be(activeSlot);
        document.RootElement.TryGetProperty("secretRef", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("primarySecretRef", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("secondarySecretRef", out _).Should().BeFalse();
    }
}