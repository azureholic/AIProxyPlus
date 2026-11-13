using AIProxyPlus.Infrastructure.Secrets;
using Azure;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AIProxyPlus.Infrastructure.Tests.Secrets;

public sealed class AzureKeyVaultSecretResolverTests
{
    private readonly IAzureKeyVaultSecretClient _client = Substitute.For<IAzureKeyVaultSecretClient>();
    private readonly ILogger<AzureKeyVaultSecretResolver> _logger =
        Substitute.For<ILogger<AzureKeyVaultSecretResolver>>();

    [Fact]
    public async Task StoreAsync_ThenResolve_UsesSameMappedName()
    {
        const string secretRef = "endpoints/88f11e6a/primary/version-1";
        const string secretValue = "secret-value";
        var expectedName = AzureKeyVaultSecretName.FromReference(secretRef);
        _client.GetAsync(expectedName, Arg.Any<CancellationToken>()).Returns(secretValue);
        var resolver = new AzureKeyVaultSecretResolver(_client, _logger);

        await resolver.StoreAsync(secretRef, secretValue);
        var resolved = await resolver.TryResolveAsync(secretRef);

        resolved.Should().Be(secretValue);
        await _client.Received(1).SetAsync(expectedName, secretValue, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryResolveAsync_MissingSecret_ReturnsNull()
    {
        _client.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new RequestFailedException(404, "Not found"));
        var resolver = new AzureKeyVaultSecretResolver(_client, _logger);

        var resolved = await resolver.TryResolveAsync("missing/reference");

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task TryResolveAsync_AuthorizationFailure_Propagates()
    {
        _client.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new RequestFailedException(403, "Forbidden"));
        var resolver = new AzureKeyVaultSecretResolver(_client, _logger);

        var act = () => resolver.TryResolveAsync("restricted/reference");

        await act.Should().ThrowAsync<RequestFailedException>();
    }

    [Fact]
    public async Task DeleteAsync_MissingSecret_DoesNotThrow()
    {
        _client.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new RequestFailedException(404, "Not found"));
        var resolver = new AzureKeyVaultSecretResolver(_client, _logger);

        var act = () => resolver.DeleteAsync("missing/reference");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Logs_NeverContainSecretValue()
    {
        const string secretValue = "do-not-log-this-value";
        var logger = new CapturingLogger<AzureKeyVaultSecretResolver>();
        var resolver = new AzureKeyVaultSecretResolver(_client, logger);

        await resolver.StoreAsync("endpoint/reference", secretValue);

        logger.Messages.Should().NotContain(message => message.Contains(secretValue, StringComparison.Ordinal));
    }

    [Fact]
    public void FromReference_DifferentReferences_ProduceDifferentValidNames()
    {
        var first = AzureKeyVaultSecretName.FromReference("endpoints/a/primary/v1");
        var second = AzureKeyVaultSecretName.FromReference("endpoints/a/primary/v2");

        first.Should().NotBe(second);
        first.Should().MatchRegex("^[a-z0-9-]{1,127}$");
        second.Should().MatchRegex("^[a-z0-9-]{1,127}$");
    }

    [Fact]
    public void FromReference_ReferenceTooLong_Throws()
    {
        var act = () => AzureKeyVaultSecretName.FromReference(new string('a', 513));

        act.Should().Throw<ArgumentException>();
    }
}
