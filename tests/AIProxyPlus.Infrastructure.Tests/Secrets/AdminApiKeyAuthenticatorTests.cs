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
    private const string RotatedKey = "rotated-admin-key-0123456789abcdef0123456789";

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
        const string configuredKey = "operator-supplied-admin-key-0123456789abcdef";
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
            secretResolver, NullLogger.Instance, RotatedKey);

        await secretResolver.Received(1).StoreAsync(
            SecretRef, AdminApiKeyAuthenticator.Hash(RotatedKey), Arg.Any<CancellationToken>());
        authenticator.Validate(RotatedKey).Should().BeTrue();
        authenticator.Validate("previously-leaked-key").Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_WithConfiguredKeyMatchingStoredHash_DoesNotStoreAgain()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync(SecretRef, Arg.Any<CancellationToken>())
            .Returns(AdminApiKeyAuthenticator.Hash(RotatedKey));

        var authenticator = await AdminApiKeyAuthenticator.CreateAsync(secretResolver, NullLogger.Instance, RotatedKey);

        authenticator.Validate(RotatedKey).Should().BeTrue();
        await secretResolver.DidNotReceive().StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("test-admin-key")]
    [InlineData("0123456789abcdef0123456789abcde")]
    public async Task CreateAsync_WithConfiguredKeyShorterThanTheMinimum_FailsWithoutStoringOrEchoingIt(string weakKey)
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync(SecretRef, Arg.Any<CancellationToken>()).Returns((string?)null);

        var act = async () => await AdminApiKeyAuthenticator.CreateAsync(secretResolver, NullLogger.Instance, weakKey);

        var exception = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Contain("Authentication:AdminApiKey").And.Contain("32");
        exception.Message.Should().NotContain(weakKey);
        await secretResolver.DidNotReceive().StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithConfiguredKeyAtTheMinimumLength_IsAccepted()
    {
        var key = new string('a', AdminApiKeyAuthenticator.MinimumConfiguredKeyLength);
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync(SecretRef, Arg.Any<CancellationToken>()).Returns((string?)null);

        var authenticator = await AdminApiKeyAuthenticator.CreateAsync(secretResolver, NullLogger.Instance, key);

        authenticator.Validate(key).Should().BeTrue();
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
