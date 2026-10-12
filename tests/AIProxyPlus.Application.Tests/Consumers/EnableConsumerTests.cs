using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Consumers;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace AIProxyPlus.Application.Tests.Consumers;

public class EnableConsumerTests
{
    private readonly IConsumerRepository _repository = Substitute.For<IConsumerRepository>();
    private readonly EnableConsumerHandler _handler;

    public EnableConsumerTests()
    {
        _handler = new EnableConsumerHandler(_repository, new EnableConsumerValidator());
    }

    [Fact]
    public async Task HandleAsync_DisabledConsumer_EnablesAndPersists()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash");
        consumer.Disable();
        _repository.GetByNameAsync(consumer.Name, Arg.Any<CancellationToken>()).Returns(consumer);

        var result = await _handler.HandleAsync(new EnableConsumerCommand(consumer.Name));

        result.Enabled.Should().BeTrue();
        result.RevokedAt.Should().BeNull();
        await _repository.Received(1).UpdateAsync(consumer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RevokedConsumer_ThrowsConflictWithoutPersisting()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "retired-team", "hash");
        consumer.Revoke();
        _repository.GetByNameAsync(consumer.Name, Arg.Any<CancellationToken>()).Returns(consumer);

        var act = () => _handler.HandleAsync(new EnableConsumerCommand(consumer.Name));

        await act.Should().ThrowAsync<ConflictException>();
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<Consumer>(), Arg.Any<CancellationToken>());
    }
}