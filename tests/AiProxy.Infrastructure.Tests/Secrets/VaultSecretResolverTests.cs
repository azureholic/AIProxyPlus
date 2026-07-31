using AiProxy.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AiProxy.Infrastructure.Tests.Secrets;

[Collection(nameof(VaultCollection))]
public class VaultSecretResolverTests(VaultContainerFixture fixture)
{
    private VaultSecretResolver CreateResolver(ILogger<VaultSecretResolver>? logger = null)
    {
        var options = new VaultOptions { Address = fixture.Address, Token = fixture.Token };
        return new VaultSecretResolver(VaultClientFactory.Create(options), options, logger ?? Substitute.For<ILogger<VaultSecretResolver>>());
    }

    [Fact]
    public async Task StoreAsync_Then_TryResolveAsync_RoundTrips()
    {
        var resolver = CreateResolver();
        var secretRef = $"endpoints/{Guid.NewGuid()}";

        await resolver.StoreAsync(secretRef, "sk-test-value");
        var resolved = await resolver.TryResolveAsync(secretRef);

        resolved.Should().Be("sk-test-value");
    }

    [Fact]
    public async Task TryResolveAsync_ForMissingPath_ReturnsNull()
    {
        var resolver = CreateResolver();

        var resolved = await resolver.TryResolveAsync($"missing/{Guid.NewGuid()}");

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task StoreAsync_CalledTwice_RotatesValue()
    {
        var resolver = CreateResolver();
        var secretRef = $"endpoints/{Guid.NewGuid()}";

        await resolver.StoreAsync(secretRef, "sk-old");
        await resolver.StoreAsync(secretRef, "sk-new");
        var resolved = await resolver.TryResolveAsync(secretRef);

        resolved.Should().Be("sk-new");
    }

    [Fact]
    public async Task DeleteAsync_Then_TryResolveAsync_ReturnsNull()
    {
        var resolver = CreateResolver();
        var secretRef = $"endpoints/{Guid.NewGuid()}";
        await resolver.StoreAsync(secretRef, "sk-to-delete");

        await resolver.DeleteAsync(secretRef);

        (await resolver.TryResolveAsync(secretRef)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ForMissingPath_DoesNotThrow()
    {
        var resolver = CreateResolver();

        var act = () => resolver.DeleteAsync($"missing/{Guid.NewGuid()}");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Logs_NeverContainTheSecretValue()
    {
        var secretRef = $"endpoints/{Guid.NewGuid()}";
        const string secretValue = "sk-super-secret-value-12345";
        var capturingLogger = new CapturingLogger<VaultSecretResolver>();
        var resolver = CreateResolver(capturingLogger);

        await resolver.StoreAsync(secretRef, secretValue);
        await resolver.TryResolveAsync(secretRef);

        capturingLogger.Messages.Should().NotBeEmpty();
        capturingLogger.Messages.Should().NotContain(m => m.Contains(secretValue));
    }
}
