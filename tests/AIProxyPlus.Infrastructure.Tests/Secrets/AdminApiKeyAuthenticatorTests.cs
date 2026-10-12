using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AIProxyPlus.Infrastructure.Tests.Secrets;

public class AdminApiKeyAuthenticatorTests
{
    [Fact]
    public async Task CreateAsync_WhenNoHashExists_GeneratesAndStoresANewOne()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("admin-api-key-hash", Arg.Any<CancellationToken>()).Returns((string?)null);

        var authenticator = await AdminApiKeyAuthenticator.CreateAsync(secretResolver, NullLogger.Instance);

        await secretResolver.Received(1).StoreAsync("admin-api-key-hash", Arg.Any<string>(), Arg.Any<CancellationToken>());
        authenticator.Validate("anything").Should().BeFalse("a freshly-generated key was never returned to the caller in this test");
    }

    [Fact]
    public async Task CreateAsync_WhenHashAlreadyExists_ReusesIt_AndDoesNotStoreAgain()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        var existingHash = AdminApiKeyAuthenticator.Hash("existing-admin-key");
        secretResolver.TryResolveAsync("admin-api-key-hash", Arg.Any<CancellationToken>()).Returns(existingHash);

        var authenticator = await AdminApiKeyAuthenticator.CreateAsync(secretResolver, NullLogger.Instance);

        authenticator.Validate("existing-admin-key").Should().BeTrue();
        await secretResolver.DidNotReceive().StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validate_WithCorrectKey_ReturnsTrue()
    {
        var authenticator = new AdminApiKeyAuthenticator(AdminApiKeyAuthenticator.Hash("correct-key"));

        authenticator.Validate("correct-key").Should().BeTrue();
    }

    [Fact]
    public void Validate_WithWrongKey_ReturnsFalse()
    {
        var authenticator = new AdminApiKeyAuthenticator(AdminApiKeyAuthenticator.Hash("correct-key"));

        authenticator.Validate("wrong-key").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithNullOrWhitespaceKey_ReturnsFalse(string? candidate)
    {
        var authenticator = new AdminApiKeyAuthenticator(AdminApiKeyAuthenticator.Hash("correct-key"));

        authenticator.Validate(candidate).Should().BeFalse();
    }

    [Fact]
    public void Hash_IsDeterministic() => AdminApiKeyAuthenticator.Hash("k").Should().Be(AdminApiKeyAuthenticator.Hash("k"));
}
