using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIProxyPlus.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AIProxyPlus.Api.Tests.Config;

public sealed class ApiRouteConfigTests : IDisposable
{
    private readonly ApiTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task FullLifecycle_NormalizesPath_RejectsDuplicate_AndProtectsAssignedRoute()
    {
        var client = _factory.CreateClient();
        var routeName = $"route-{Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/config/api-routes",
            new { name = routeName, provider = "OpenAI", pathPrefix = " openai//v1/ " });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var routeId = createdDocument.RootElement.GetProperty("id").GetGuid();
        createdDocument.RootElement.GetProperty("pathPrefix").GetString().Should().Be("/openai/v1");

        var duplicateResponse = await client.PostAsJsonAsync(
            "/api/v1/config/api-routes",
            new { name = $"duplicate-{Guid.NewGuid():N}", provider = "OpenAI", pathPrefix = "/openai/v1" });
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var updatedName = $"updated-{Guid.NewGuid():N}";
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/config/api-routes/{routeId}",
            new { name = updatedName, provider = "MicrosoftAI", pathPrefix = "/openai/deployments" });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var modelName = $"model-{Guid.NewGuid():N}";
        (await client.PostAsJsonAsync("/api/v1/config/models", new { name = modelName }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var endpointResponse = await client.PostAsJsonAsync(
            "/api/v1/config/endpoints",
            new
            {
                name = $"endpoint-{Guid.NewGuid():N}",
                baseUrl = "https://backend.example.com",
                authMode = "ApiKey",
                apiKeyPlaintext = "sk-test",
            });
        using var endpointDocument = JsonDocument.Parse(await endpointResponse.Content.ReadAsStringAsync());
        var endpointId = endpointDocument.RootElement.GetProperty("id").GetGuid();

        var assignmentResponse = await client.PostAsJsonAsync(
            $"/api/v1/config/models/{modelName}/endpoints",
            new { endpointId, apiRouteId = routeId, priority = 1 });
        using var assignmentDocument = JsonDocument.Parse(await assignmentResponse.Content.ReadAsStringAsync());
        var assignmentId = assignmentDocument.RootElement.GetProperty("id").GetGuid();

        (await client.DeleteAsync($"/api/v1/config/api-routes/{routeId}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.DeleteAsync($"/api/v1/config/models/{modelName}/endpoints/{assignmentId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/v1/config/api-routes/{routeId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/config/api-routes/{routeId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}