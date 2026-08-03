using AiProxy.Application.Abstractions;
using AiProxy.Application.Consumers;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Consumers;

public class RotateConsumerKeyTests
{
    private readonly IConsumerRepository _repository = Substitute.For<IConsumerRepository>();
    private readonly IApiKeyGenerator _keyGenerator = Substitute.For<IApiKeyGenerator>();
    private readonly IApiKeyHasher _hasher = Substitute.For<IApiKeyHasher>();
    private readonly RotateConsumerKeyHandler _handler;

    public RotateConsumerKeyTests()
    {
        _handler = new RotateConsumerKeyHandler(_repository, _keyGenerator, _hasher, new RotateConsumerKeyValidator());
    }

    [Fact]
    public async Task HandleAsync_WithExistingConsumer_RotatesHashAndReturnsNewPlaintext()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "old-hash");
        _repository.GetByNameAsync("team-checkout", Arg.Any<CancellationToken>()).Returns(consumer);
        _keyGenerator.Generate().Returns("aip_new-key");
        _hasher.Hash("aip_new-key").Returns("new-hash");

        var result = await _handler.HandleAsync(new RotateConsumerKeyCommand("team-checkout"));

        result.PlaintextApiKey.Should().Be("aip_new-key");
        consumer.ApiKeyHash.Should().Be("new-hash");
        await _repository.Received(1).UpdateAsync(consumer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithUnknownConsumer_ThrowsNotFoundException()
    {
        _repository.GetByNameAsync("missing", Arg.Any<CancellationToken>()).Returns((Consumer?)null);

        var act = () => _handler.HandleAsync(new RotateConsumerKeyCommand("missing"));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_WithOidcConsumer_ThrowsWithoutGeneratingKey()
    {
        var consumer = Consumer.CreateOidc(
            Guid.NewGuid(),
            "proxy-admin",
            "https://identity.example.com/realms/ai-proxy",
            "subject-1");
        _repository.GetByNameAsync("proxy-admin", Arg.Any<CancellationToken>()).Returns(consumer);

        var act = () => _handler.HandleAsync(new RotateConsumerKeyCommand("proxy-admin"));

        await act.Should().ThrowAsync<ConflictException>();
        _keyGenerator.DidNotReceive().Generate();
    }
}
