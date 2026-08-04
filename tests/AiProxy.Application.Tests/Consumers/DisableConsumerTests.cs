using AiProxy.Application.Abstractions;
using AiProxy.Application.Consumers;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Consumers;

public class DisableConsumerTests
{
    private readonly IConsumerRepository _repository = Substitute.For<IConsumerRepository>();
    private readonly DisableConsumerHandler _handler;

    public DisableConsumerTests()
    {
        _handler = new DisableConsumerHandler(_repository, new DisableConsumerValidator());
    }

    [Fact]
    public async Task HandleAsync_ActiveConsumer_DisablesAndPersists()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash");
        _repository.GetByNameAsync(consumer.Name, Arg.Any<CancellationToken>()).Returns(consumer);

        var result = await _handler.HandleAsync(new DisableConsumerCommand(consumer.Name));

        result.Enabled.Should().BeFalse();
        result.RevokedAt.Should().BeNull();
        await _repository.Received(1).UpdateAsync(consumer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RevokedConsumer_ThrowsConflictWithoutPersisting()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "retired-team", "hash");
        consumer.Revoke();
        _repository.GetByNameAsync(consumer.Name, Arg.Any<CancellationToken>()).Returns(consumer);

        var act = () => _handler.HandleAsync(new DisableConsumerCommand(consumer.Name));

        await act.Should().ThrowAsync<ConflictException>();
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<Consumer>(), Arg.Any<CancellationToken>());
    }
}