using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AiProxy.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AiProxy.Api.Tests.Config;

public class ConsumerConfigTests : IDisposable
{
    private readonly ApiTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Create_ReturnsPlaintextKeyOnce_AndListNeverExposesKeyOrHash()
    {
        var client = _factory.CreateClient();
        var consumerName = $"team-{Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = consumerName });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var plaintextKey = created.RootElement.GetProperty("apiKey").GetString();
        plaintextKey.Should().NotBeNullOrEmpty();
        created.RootElement.GetProperty("consumer").GetProperty("name").GetString().Should().Be(consumerName);

        var listResponse = await client.GetAsync("/api/v1/config/consumers");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listBody.Should().Contain(consumerName);
        listBody.Should().NotContain(plaintextKey!, "the plaintext key must only ever be shown once, at creation");
        listBody.Should().NotContain("apiKeyHash", "GET must never expose the key or its hash");
    }

    [Fact]
    public async Task CreatedConsumer_CanAuthenticateIngressRequests()
    {
        var client = _factory.CreateClient();
        var consumerName = $"team-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = consumerName });
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var plaintextKey = created.RootElement.GetProperty("apiKey").GetString()!;

        var request = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = "never-registered", messages = Array.Empty<object>() }),
        };
        request.Headers.Remove("X-Api-Key");
        request.Headers.Add("X-Api-Key", plaintextKey);

        var response = await client.SendAsync(request);

        // 404 (unknown model) proves auth succeeded and the request reached routing, not 401.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rotate_InvalidatesOldKey_AndIssuesNewOne()
    {
        var client = _factory.CreateClient();
        var consumerName = $"team-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = consumerName });
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var oldKey = created.RootElement.GetProperty("apiKey").GetString()!;

        var rotateResponse = await client.PutAsync($"/api/v1/config/consumers/{consumerName}/rotate", new StringContent(string.Empty));

        rotateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var rotated = JsonDocument.Parse(await rotateResponse.Content.ReadAsStringAsync());
        var newKey = rotated.RootElement.GetProperty("apiKey").GetString()!;
        newKey.Should().NotBe(oldKey);

        var oldKeyRequest = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = "x", messages = Array.Empty<object>() }),
        };
        oldKeyRequest.Headers.Remove("X-Api-Key");
        oldKeyRequest.Headers.Add("X-Api-Key", oldKey);

        var oldKeyResponse = await client.SendAsync(oldKeyRequest);
        oldKeyResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoke_DisablesConsumer_AndIngressRequestsAreRejected()
    {
        var client = _factory.CreateClient();
        var consumerName = $"team-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = consumerName });
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var plaintextKey = created.RootElement.GetProperty("apiKey").GetString()!;

        var revokeResponse = await client.DeleteAsync($"/api/v1/config/consumers/{consumerName}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var revoked = JsonDocument.Parse(await revokeResponse.Content.ReadAsStringAsync());
        revoked.RootElement.GetProperty("enabled").GetBoolean().Should().BeFalse();

        var request = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = "x", messages = Array.Empty<object>() }),
        };
        request.Headers.Remove("X-Api-Key");
        request.Headers.Add("X-Api-Key", plaintextKey);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
