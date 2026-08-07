using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AiProxy.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AiProxy.Api.Tests.Config;

public class ModelConfigTests : IDisposable
{
    private readonly ApiTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task FullLifecycle_Create_AddEndpoint_UpdateEndpoint_SetSecret_RemoveEndpoint_Delete()
    {
        var client = _factory.CreateClient();
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var endpointName = $"backend-{Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/config/models", new { name = modelName });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        created.RootElement.GetProperty("name").GetString().Should().Be(modelName);

        var getResponse = await client.GetAsync($"/api/v1/config/models/{modelName}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var createEndpointResponse = await client.PostAsJsonAsync(
            "/api/v1/config/endpoints",
            new
            {
                name = endpointName,
                baseUrl = "https://backend.example.com",
                authMode = "ApiKey",
                apiKeyPlaintext = "sk-test-123",
            });
        createEndpointResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var endpointDoc = JsonDocument.Parse(await createEndpointResponse.Content.ReadAsStringAsync());
        var endpointId = endpointDoc.RootElement.GetProperty("id").GetGuid();
        endpointDoc.RootElement.GetProperty("hasPrimarySecret").GetBoolean().Should().BeTrue();
        endpointDoc.RootElement.GetProperty("hasSecondarySecret").GetBoolean().Should().BeFalse();
        endpointDoc.RootElement.GetProperty("activeCredentialSlot").GetString().Should().Be("Primary");
        endpointDoc.RootElement.TryGetProperty("secretRef", out _).Should().BeFalse();
        endpointDoc.RootElement.TryGetProperty("primarySecretRef", out _).Should().BeFalse();
        endpointDoc.RootElement.TryGetProperty("secondarySecretRef", out _).Should().BeFalse();
        endpointDoc.RootElement.ToString().Should().NotContain("sk-test-123", "the plaintext key must never be echoed back");

        var createApiRouteResponse = await client.PostAsJsonAsync(
            "/api/v1/config/api-routes",
            new { name = $"native-openai-{Guid.NewGuid():N}", provider = "OpenAI", pathPrefix = "/openai/v1" });
        createApiRouteResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var apiRouteDoc = JsonDocument.Parse(await createApiRouteResponse.Content.ReadAsStringAsync());
        var apiRouteId = apiRouteDoc.RootElement.GetProperty("id").GetGuid();

        var addEndpointResponse = await client.PostAsJsonAsync(
            $"/api/v1/config/models/{modelName}/endpoints",
            new { endpointId, apiRouteId, priority = 1 });
        addEndpointResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var assignmentDoc = JsonDocument.Parse(await addEndpointResponse.Content.ReadAsStringAsync());
        var assignmentId = assignmentDoc.RootElement.GetProperty("id").GetGuid();

        var updateEndpointResponse = await client.PutAsJsonAsync(
            $"/api/v1/config/models/{modelName}/endpoints/{assignmentId}", new { priority = 2 });
        updateEndpointResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var updatedEndpointDoc = JsonDocument.Parse(await updateEndpointResponse.Content.ReadAsStringAsync());
        updatedEndpointDoc.RootElement.GetProperty("priority").GetInt32().Should().Be(2);

        var updateEndpointConfigurationResponse = await client.PutAsJsonAsync(
            $"/api/v1/config/endpoints/{endpointId}",
            new
            {
                name = endpointName,
                baseUrl = "https://backend.example.com",
                timeoutSeconds = 30,
                maxRetries = 2,
            });
        updateEndpointConfigurationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var updatedEndpointConfigurationDoc = JsonDocument.Parse(
            await updateEndpointConfigurationResponse.Content.ReadAsStringAsync());
        updatedEndpointConfigurationDoc.RootElement.GetProperty("timeoutSeconds").GetInt32().Should().Be(30);

        var setSecretResponse = await client.PutAsJsonAsync(
            $"/api/v1/config/endpoints/{endpointId}/secret",
            new { apiKeyPlaintext = "sk-test-rotated" });
        setSecretResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var removeEndpointResponse = await client.DeleteAsync(
            $"/api/v1/config/models/{modelName}/endpoints/{assignmentId}");
        removeEndpointResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var deleteEndpointResponse = await client.DeleteAsync($"/api/v1/config/endpoints/{endpointId}");
        deleteEndpointResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var deleteResponse = await client.DeleteAsync($"/api/v1/config/models/{modelName}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAfterDelete = await client.GetAsync($"/api/v1/config/models/{modelName}");
        getAfterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReusableEndpoint_AssignToMultipleModels_DeleteRequiresAllAssignmentsRemoved()
    {
        var client = _factory.CreateClient();
        var firstModelName = $"first-{Guid.NewGuid():N}";
        var secondModelName = $"second-{Guid.NewGuid():N}";

        (await client.PostAsJsonAsync("/api/v1/config/models", new { name = firstModelName }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/v1/config/models", new { name = secondModelName }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var createEndpointResponse = await client.PostAsJsonAsync(
            "/api/v1/config/endpoints",
            new
            {
                name = $"shared-{Guid.NewGuid():N}",
                baseUrl = "https://shared.example.com",
                authMode = "ApiKey",
                apiKeyPlaintext = "sk-shared",
            });
        using var endpointDoc = JsonDocument.Parse(await createEndpointResponse.Content.ReadAsStringAsync());
        var endpointId = endpointDoc.RootElement.GetProperty("id").GetGuid();

        var createApiRouteResponse = await client.PostAsJsonAsync(
            "/api/v1/config/api-routes",
            new { name = $"shared-route-{Guid.NewGuid():N}", provider = "OpenAI", pathPrefix = "/openai/v1" });
        using var apiRouteDoc = JsonDocument.Parse(await createApiRouteResponse.Content.ReadAsStringAsync());
        var apiRouteId = apiRouteDoc.RootElement.GetProperty("id").GetGuid();

        var firstAssignmentResponse = await client.PostAsJsonAsync(
            $"/api/v1/config/models/{firstModelName}/endpoints",
            new { endpointId, apiRouteId, priority = 1 });
        firstAssignmentResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var firstAssignmentDoc = JsonDocument.Parse(await firstAssignmentResponse.Content.ReadAsStringAsync());
        var firstAssignmentId = firstAssignmentDoc.RootElement.GetProperty("id").GetGuid();

        var secondAssignmentResponse = await client.PostAsJsonAsync(
            $"/api/v1/config/models/{secondModelName}/endpoints",
            new { endpointId, apiRouteId, priority = 2 });
        secondAssignmentResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var secondAssignmentDoc = JsonDocument.Parse(await secondAssignmentResponse.Content.ReadAsStringAsync());
        var secondAssignmentId = secondAssignmentDoc.RootElement.GetProperty("id").GetGuid();

        (await client.DeleteAsync($"/api/v1/config/endpoints/{endpointId}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.DeleteAsync($"/api/v1/config/models/{firstModelName}/endpoints/{firstAssignmentId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/v1/config/models/{secondModelName}/endpoints/{secondAssignmentId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/v1/config/endpoints/{endpointId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CreatingDuplicateName_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";

        (await client.PostAsJsonAsync("/api/v1/config/models", new { name = modelName })).StatusCode
            .Should().Be(HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync("/api/v1/config/models", new { name = modelName });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreatingWithEmptyName_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/config/models", new { name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetUnknownModel_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/config/models/never-created");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListModels_IncludesCreatedModel()
    {
        var client = _factory.CreateClient();
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/v1/config/models", new { name = modelName });

        var response = await client.GetAsync("/api/v1/config/models");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(modelName);
    }
}
