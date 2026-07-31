using AiProxy.Application.Abstractions;
using AiProxy.Application.Consumers;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Consumers;

public class CreateConsumerTests
{
    private readonly IConsumerRepository _repository = Substitute.For<IConsumerRepository>();
    private readonly IApiKeyGenerator _keyGenerator = Substitute.For<IApiKeyGenerator>();
    private readonly IApiKeyHasher _hasher = Substitute.For<IApiKeyHasher>();
    private readonly CreateConsumerHandler _handler;

    public CreateConsumerTests()
    {
        _handler = new CreateConsumerHandler(_repository, _keyGenerator, _hasher, new CreateConsumerValidator());
    }

    [Fact]
    public async Task HandleAsync_WithNewName_GeneratesKeyStoresHashAndReturnsPlaintextOnce()
    {
        _repository.ExistsByNameAsync("team-checkout", Arg.Any<CancellationToken>()).Returns(false);
        _keyGenerator.Generate().Returns("aip_plaintext-key");
        _hasher.Hash("aip_plaintext-key").Returns("hashed-value");

        var result = await _handler.HandleAsync(new CreateConsumerCommand("team-checkout"));

        result.PlaintextApiKey.Should().Be("aip_plaintext-key");
        result.Consumer.ApiKeyHash.Should().Be("hashed-value");
        await _repository.Received(1).AddAsync(
            Arg.Is<Consumer>(c => c.Name == "team-checkout" && c.ApiKeyHash == "hashed-value"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithExistingName_ThrowsConflictException()
    {
        _repository.ExistsByNameAsync("team-checkout", Arg.Any<CancellationToken>()).Returns(true);

        var act = () => _handler.HandleAsync(new CreateConsumerCommand("team-checkout"));

        await act.Should().ThrowAsync<ConflictException>();
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}
