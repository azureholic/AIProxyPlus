using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Consumers;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace AIProxyPlus.Application.Tests.Consumers;

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
    public async Task HandleAsync_WithNewName_GeneratesBothKeysStoresHashesAndReturnsPlaintextsOnce()
    {
        _repository.ExistsByNameAsync("team-checkout", Arg.Any<CancellationToken>()).Returns(false);
        _keyGenerator.Generate().Returns("aip_key-1", "aip_key-2");
        _hasher.Hash("aip_key-1").Returns("hash-1");
        _hasher.Hash("aip_key-2").Returns("hash-2");

        var result = await _handler.HandleAsync(new CreateConsumerCommand("team-checkout"));

        result.ApiKey1.Should().Be("aip_key-1");
        result.ApiKey2.Should().Be("aip_key-2");
        result.Consumer.ApiKeyHash.Should().Be("hash-1");
        result.Consumer.ApiKeyHash2.Should().Be("hash-2");
        await _repository.Received(1).AddAsync(
            Arg.Is<Consumer>(c => c.Name == "team-checkout" && c.ApiKeyHash == "hash-1" && c.ApiKeyHash2 == "hash-2"),
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
