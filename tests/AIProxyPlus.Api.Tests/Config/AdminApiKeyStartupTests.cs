using AIProxyPlus.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AIProxyPlus.Api.Tests.Config;

public sealed class AdminApiKeyStartupTests : IDisposable
{
    private readonly ApiTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void Startup_WithConfiguredAdminKeyShorterThanTheMinimum_FailsBeforeServingAnyRequest()
    {
        const string weakKey = "weak-key-value";
        using var factory = _factory.WithWebHostBuilder(
            builder => builder.UseSetting("Authentication:AdminApiKey", weakKey));

        var act = () => factory.CreateClient();

        var exception = act.Should().Throw<Exception>().Which;
        exception.ToString().Should().Contain("Authentication:AdminApiKey must be at least 32 characters");
        exception.ToString().Should().NotContain(weakKey);
    }

    [Fact]
    public async Task Startup_WithStrongConfiguredAdminKey_StartsAndAuthenticatesTheAdminSurface()
    {
        var strongKey = new string('b', 48);
        using var factory = _factory.WithWebHostBuilder(
            builder => builder.UseSetting("Authentication:AdminApiKey", strongKey));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove("X-Admin-Api-Key");
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", strongKey);

        var response = await client.GetAsync("/api/v1/config/models");

        response.IsSuccessStatusCode.Should().BeTrue("the configured key replaces the stored hash and authenticates");
    }
}
