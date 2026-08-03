using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AiProxy.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AiProxy.Api.Tests.Config;

public class ModelDeploymentConfigTests : IDisposable
{
    private readonly ApiTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task FullLifecycle_Create_AddEndpoint_UpdateEndpoint_SetSecret_RemoveEndpoint_Delete()
    {
        var client = _factory.CreateClient();
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var endpointName = $"backend-{Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/config/model-deployments", new { name = deploymentName });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        created.RootElement.GetProperty("name").GetString().Should().Be(deploymentName);

        var getResponse = await client.GetAsync($"/api/v1/config/model-deployments/{deploymentName}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var createEndpointResponse = await client.PostAsJsonAsync(
            "/api/v1/config/endpoints",
            new
            {
                name = endpointName,
                baseUrl = "https://backend.example.com",
                apiFamily = "OpenAI",
                authMode = "ApiKey",
                apiKeyPlaintext = "sk-test-123",
            });
        createEndpointResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var endpointDoc = JsonDocument.Parse(await createEndpointResponse.Content.ReadAsStringAsync());
        var endpointId = endpointDoc.RootElement.GetProperty("id").GetGuid();
        endpointDoc.RootElement.TryGetProperty("secretRef", out var secretRefProp).Should().BeTrue();
        secretRefProp.GetString().Should().NotBeNullOrEmpty();
        endpointDoc.RootElement.ToString().Should().NotContain("sk-test-123", "the plaintext key must never be echoed back");

        var addEndpointResponse = await client.PostAsJsonAsync(
            $"/api/v1/config/model-deployments/{deploymentName}/endpoints",
            new { endpointId, priority = 1 });
        addEndpointResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var updateEndpointResponse = await client.PutAsJsonAsync(
            $"/api/v1/config/model-deployments/{deploymentName}/endpoints/{endpointId}", new { priority = 2 });
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
            $"/api/v1/config/model-deployments/{deploymentName}/endpoints/{endpointId}");
        removeEndpointResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var deleteEndpointResponse = await client.DeleteAsync($"/api/v1/config/endpoints/{endpointId}");
        deleteEndpointResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var deleteResponse = await client.DeleteAsync($"/api/v1/config/model-deployments/{deploymentName}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAfterDelete = await client.GetAsync($"/api/v1/config/model-deployments/{deploymentName}");
        getAfterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReusableEndpoint_AssignToMultipleDeployments_DeleteRequiresAllAssignmentsRemoved()
    {
        var client = _factory.CreateClient();
        var firstDeploymentName = $"first-{Guid.NewGuid():N}";
        var secondDeploymentName = $"second-{Guid.NewGuid():N}";

        (await client.PostAsJsonAsync("/api/v1/config/model-deployments", new { name = firstDeploymentName }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/v1/config/model-deployments", new { name = secondDeploymentName }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var createEndpointResponse = await client.PostAsJsonAsync(
            "/api/v1/config/endpoints",
            new
            {
                name = $"shared-{Guid.NewGuid():N}",
                baseUrl = "https://shared.example.com",
                apiFamily = "OpenAI",
                authMode = "ApiKey",
                apiKeyPlaintext = "sk-shared",
            });
        using var endpointDoc = JsonDocument.Parse(await createEndpointResponse.Content.ReadAsStringAsync());
        var endpointId = endpointDoc.RootElement.GetProperty("id").GetGuid();

        (await client.PostAsJsonAsync(
            $"/api/v1/config/model-deployments/{firstDeploymentName}/endpoints",
            new { endpointId, priority = 1 })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync(
            $"/api/v1/config/model-deployments/{secondDeploymentName}/endpoints",
            new { endpointId, priority = 2 })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.DeleteAsync($"/api/v1/config/endpoints/{endpointId}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.DeleteAsync($"/api/v1/config/model-deployments/{firstDeploymentName}/endpoints/{endpointId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/v1/config/model-deployments/{secondDeploymentName}/endpoints/{endpointId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/v1/config/endpoints/{endpointId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CreatingDuplicateName_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";

        (await client.PostAsJsonAsync("/api/v1/config/model-deployments", new { name = deploymentName })).StatusCode
            .Should().Be(HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync("/api/v1/config/model-deployments", new { name = deploymentName });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreatingWithEmptyName_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/config/model-deployments", new { name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetUnknownDeployment_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/config/model-deployments/never-created");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListDeployments_IncludesCreatedDeployment()
    {
        var client = _factory.CreateClient();
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/v1/config/model-deployments", new { name = deploymentName });

        var response = await client.GetAsync("/api/v1/config/model-deployments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(deploymentName);
    }
}
