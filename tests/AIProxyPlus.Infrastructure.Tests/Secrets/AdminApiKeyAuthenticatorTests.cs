using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AIProxyPlus.Infrastructure.Tests.Secrets;

public class AdminApiKeyAuthenticatorTests
{
    private const string SecretRef = "admin-api-key-hash";

    [Fact]
    public async Task CreateAsync_WhenNoHashAndNoConfiguredKey_GeneratesNothingAndLogsNoKeyMaterial()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync(SecretRef, Arg.Any<CancellationToken>()).Returns((string?)null);
        var logger = new CapturingLogger();

        var authenticator = await AdminApiKeyAuthenticator.CreateAsync(secretResolver, logger);

        await secretResolver.DidNotReceive().StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        authenticator.Validate("anything").Should().BeFalse();
        authenticator.Validate("aip_admin_").Should().BeFalse();
        logger.Messages.Should().ContainSingle().Which.Should().Contain("Authentication:AdminApiKey");
        logger.Messages.Should().AllSatisfy(message => message.Should().NotContain("aip_admin_"));
    }

    [Fact]
    public async Task CreateAsync_WithConfiguredKey_StoresOnlyItsHash_AndNeverLogsTheKey()
    {
        const string configuredKey = "operator-supplied-admin-key";
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync(SecretRef, Arg.Any<CancellationToken>()).Returns((string?)null);
        var logger = new CapturingLogger();

        var authenticator = await AdminApiKeyAuthenticator.CreateAsync(secretResolver, logger, configuredKey);

        await secretResolver.Received(1).StoreAsync(
            SecretRef, AdminApiKeyAuthenticator.Hash(configuredKey), Arg.Any<CancellationToken>());
        authenticator.Validate(configuredKey).Should().BeTrue();
        authenticator.Validate("another-key").Should().BeFalse();
        logger.Messages.Should().AllSatisfy(message => message.Should().NotContain(configuredKey));
    }

    [Fact]
    public async Task CreateAsync_WithConfiguredKeyDifferingFromStoredHash_ReplacesTheStoredHash()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync(SecretRef, Arg.Any<CancellationToken>())
            .Returns(AdminApiKeyAuthenticator.Hash("previously-leaked-key"));

        var authenticator = await AdminApiKeyAuthenticator.CreateAsync(
            secretResolver, NullLogger.Instance, "rotated-admin-key");

        await secretResolver.Received(1).StoreAsync(
            SecretRef, AdminApiKeyAuthenticator.Hash("rotated-admin-key"), Arg.Any<CancellationToken>());
        authenticator.Validate("rotated-admin-key").Should().BeTrue();
        authenticator.Validate("previously-leaked-key").Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_WithConfiguredKeyMatchingStoredHash_DoesNotStoreAgain()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync(SecretRef, Arg.Any<CancellationToken>())
            .Returns(AdminApiKeyAuthenticator.Hash("same-key"));

        var authenticator = await AdminApiKeyAuthenticator.CreateAsync(secretResolver, NullLogger.Instance, "same-key");

        authenticator.Validate("same-key").Should().BeTrue();
        await secretResolver.DidNotReceive().StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WhenHashAlreadyExists_ReusesIt_AndDoesNotStoreAgain()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        var existingHash = AdminApiKeyAuthenticator.Hash("existing-admin-key");
        secretResolver.TryResolveAsync(SecretRef, Arg.Any<CancellationToken>()).Returns(existingHash);

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

    [Fact]
    public void Validate_WhenNoKeyIsConfigured_RejectsEveryCandidate()
    {
        var authenticator = new AdminApiKeyAuthenticator(string.Empty);

        authenticator.Validate("anything").Should().BeFalse();
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
