using System.Net;
using AiProxy.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AiProxy.Api.Tests;

public class HealthCheckTests : IDisposable
{
    private readonly ApiTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Live_AlwaysReturnsOk()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_ReturnsOk_WhenDependenciesAreHealthy()
    {
        var response = await _factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"vault\":\"healthy\"");
        body.Should().Contain("\"postgres\":\"healthy\"");
    }
}
