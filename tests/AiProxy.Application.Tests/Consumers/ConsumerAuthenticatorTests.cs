using AiProxy.Application.Abstractions;
using AiProxy.Application.Consumers;
using AiProxy.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Consumers;

public class ConsumerAuthenticatorTests
{
    private readonly IConsumerRepository _repository = Substitute.For<IConsumerRepository>();
    private readonly IApiKeyHasher _hasher = Substitute.For<IApiKeyHasher>();
    private readonly ConsumerAuthenticator _authenticator;

    public ConsumerAuthenticatorTests()
    {
        _authenticator = new ConsumerAuthenticator(_repository, _hasher);
    }

    [Fact]
    public async Task AuthenticateAsync_WithValidEnabledConsumerKey_ReturnsConsumer()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-of-key");
        _hasher.Hash("plaintext-key").Returns("hash-of-key");
        _repository.GetByApiKeyHashAsync("hash-of-key", Arg.Any<CancellationToken>()).Returns(consumer);

        var result = await _authenticator.AuthenticateAsync("plaintext-key");

        result.Should().Be(consumer);
    }

    [Fact]
    public async Task AuthenticateAsync_WithUnknownHash_ReturnsNull()
    {
        _hasher.Hash("plaintext-key").Returns("hash-of-key");
        _repository.GetByApiKeyHashAsync("hash-of-key", Arg.Any<CancellationToken>()).Returns((Consumer?)null);

        var result = await _authenticator.AuthenticateAsync("plaintext-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_WithRevokedConsumer_ReturnsNull()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-of-key");
        consumer.Revoke();
        _hasher.Hash("plaintext-key").Returns("hash-of-key");
        _repository.GetByApiKeyHashAsync("hash-of-key", Arg.Any<CancellationToken>()).Returns(consumer);

        var result = await _authenticator.AuthenticateAsync("plaintext-key");

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AuthenticateAsync_WithEmptyKey_ReturnsNullWithoutHashing(string plaintextApiKey)
    {
        var result = await _authenticator.AuthenticateAsync(plaintextApiKey);

        result.Should().BeNull();
        _hasher.DidNotReceiveWithAnyArgs().Hash(default!);
    }
}
