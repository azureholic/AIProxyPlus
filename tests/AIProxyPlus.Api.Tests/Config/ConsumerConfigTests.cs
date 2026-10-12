using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIProxyPlus.Api.Tests.Infrastructure;
using AIProxyPlus.Domain.Entities;
using FluentAssertions;

namespace AIProxyPlus.Api.Tests.Config;

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
        var plaintextKey1 = created.RootElement.GetProperty("apiKey1").GetString();
        var plaintextKey2 = created.RootElement.GetProperty("apiKey2").GetString();
        plaintextKey1.Should().NotBeNullOrEmpty();
        plaintextKey2.Should().NotBeNullOrEmpty().And.NotBe(plaintextKey1);
        created.RootElement.GetProperty("consumer").GetProperty("name").GetString().Should().Be(consumerName);

        var listResponse = await client.GetAsync("/api/v1/config/consumers");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listBody.Should().Contain(consumerName);
        listBody.Should().NotContain(plaintextKey1!, "plaintext keys must only ever be shown once, at creation");
        listBody.Should().NotContain(plaintextKey2!);
        listBody.Should().NotContain("apiKeyHash", "GET must never expose the key or its hash");
    }

    [Fact]
    public async Task CreatedConsumer_CanAuthenticateIngressRequests()
    {
        var client = _factory.CreateClient();
        var consumerName = $"team-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = consumerName });
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var plaintextKey = created.RootElement.GetProperty("apiKey1").GetString()!;

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
    public async Task Rotate_OneSlot_InvalidatesOnlyThatSlot_AndLeavesOtherKeyValid()
    {
        var client = _factory.CreateClient();
        var consumerName = $"team-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = consumerName });
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var oldKey1 = created.RootElement.GetProperty("apiKey1").GetString()!;
        var key2 = created.RootElement.GetProperty("apiKey2").GetString()!;

        var rotateResponse = await client.PutAsync(
            $"/api/v1/config/consumers/{consumerName}/rotate?slot=1", new StringContent(string.Empty));

        rotateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var rotated = JsonDocument.Parse(await rotateResponse.Content.ReadAsStringAsync());
        var newKey = rotated.RootElement.GetProperty("apiKey").GetString()!;
        newKey.Should().NotBe(oldKey1);

        var oldKeyRequest = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = "x", messages = Array.Empty<object>() }),
        };
        oldKeyRequest.Headers.Remove("X-Api-Key");
        oldKeyRequest.Headers.Add("X-Api-Key", oldKey1);

        var oldKeyResponse = await client.SendAsync(oldKeyRequest);
        oldKeyResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var untouchedKeyRequest = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = "x", messages = Array.Empty<object>() }),
        };
        untouchedKeyRequest.Headers.Add("X-Api-Key", key2);

        var untouchedKeyResponse = await client.SendAsync(untouchedKeyRequest);
        untouchedKeyResponse.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the untouched key must remain valid while the other slot rotates");
    }

    [Fact]
    public async Task Rotate_WithoutSlot_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsync(
            "/api/v1/config/consumers/any/rotate", new StringContent(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AllowedModels_DeniesDisallowedModel_AndPermitsAllowedModelToReachRouting()
    {
        var allowedModel = Model.Create(Guid.NewGuid(), $"allowed-{Guid.NewGuid():N}");
        var deniedModel = Model.Create(Guid.NewGuid(), $"denied-{Guid.NewGuid():N}");
        _factory.Models.Seed(allowedModel);
        _factory.Models.Seed(deniedModel);

        var client = _factory.CreateClient();
        var consumerName = $"team-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = consumerName });
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var apiKey = created.RootElement.GetProperty("apiKey1").GetString()!;

        var allowResponse = await client.PutAsync(
            $"/api/v1/config/consumers/{consumerName}/allowed-models/{allowedModel.Name}",
            new StringContent(string.Empty));
        allowResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deniedResponse = await SendModelRequestAsync(client, apiKey, deniedModel.Name);
        deniedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var allowedResponse = await SendModelRequestAsync(client, apiKey, allowedModel.Name);
        allowedResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "503 proves authorization passed and routing reached the allowed model, which has no endpoints");
    }

    [Fact]
    public async Task Revoke_DisablesConsumer_AndIngressRequestsAreRejected()
    {
        var client = _factory.CreateClient();
        var consumerName = $"team-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = consumerName });
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var plaintextKey = created.RootElement.GetProperty("apiKey1").GetString()!;

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

    [Fact]
    public async Task DisableThenEnable_PreservesKeyAndRestoresIngressAccess()
    {
        var client = _factory.CreateClient();
        var consumerName = $"team-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = consumerName });
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var plaintextKey = created.RootElement.GetProperty("apiKey1").GetString()!;

        var disableResponse = await client.PutAsync(
            $"/api/v1/config/consumers/{consumerName}/disable", new StringContent(string.Empty));

        disableResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var disabled = JsonDocument.Parse(await disableResponse.Content.ReadAsStringAsync());
        disabled.RootElement.GetProperty("enabled").GetBoolean().Should().BeFalse();
        disabled.RootElement.GetProperty("revokedAt").ValueKind.Should().Be(JsonValueKind.Null);

        var deniedResponse = await SendModelRequestAsync(client, plaintextKey, "never-registered");
        deniedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var enableResponse = await client.PutAsync(
            $"/api/v1/config/consumers/{consumerName}/enable", new StringContent(string.Empty));

        enableResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var restoredResponse = await SendModelRequestAsync(client, plaintextKey, "never-registered");
        restoredResponse.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the original key should authenticate again after the consumer is enabled");
    }

    [Fact]
    public async Task Enable_RevokedConsumer_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var consumerName = $"team-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = consumerName });
        await client.DeleteAsync($"/api/v1/config/consumers/{consumerName}");

        var response = await client.PutAsync(
            $"/api/v1/config/consumers/{consumerName}/enable", new StringContent(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeletePermanently_DisabledConsumer_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var consumerName = $"team-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = consumerName });
        await client.PutAsync($"/api/v1/config/consumers/{consumerName}/disable", new StringContent(string.Empty));

        var response = await client.DeleteAsync($"/api/v1/config/consumers/{consumerName}/permanent");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeletePermanently_RevokedConsumer_RemovesConsumerFromList()
    {
        var client = _factory.CreateClient();
        var consumerName = $"team-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = consumerName });
        await client.DeleteAsync($"/api/v1/config/consumers/{consumerName}");

        var deleteResponse = await client.DeleteAsync($"/api/v1/config/consumers/{consumerName}/permanent");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var consumers = await client.GetFromJsonAsync<JsonElement>("/api/v1/config/consumers");
        consumers.EnumerateArray().Should().NotContain(element => element.GetProperty("name").GetString() == consumerName);
    }

    [Fact]
    public async Task DeletePermanently_ActiveConsumer_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var consumerName = $"team-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/v1/config/consumers", new { name = consumerName });

        var response = await client.DeleteAsync($"/api/v1/config/consumers/{consumerName}/permanent");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static Task<HttpResponseMessage> SendModelRequestAsync(HttpClient client, string apiKey, string modelName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = modelName, messages = Array.Empty<object>() }),
        };
        request.Headers.Add("X-Api-Key", apiKey);
        return client.SendAsync(request);
    }
}
