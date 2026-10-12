using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Consumers;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace AIProxyPlus.Application.Tests.Consumers;

public class DeleteConsumerPermanentlyTests
{
    private readonly IConsumerRepository _repository = Substitute.For<IConsumerRepository>();
    private readonly DeleteConsumerPermanentlyHandler _handler;

    public DeleteConsumerPermanentlyTests()
    {
        _handler = new DeleteConsumerPermanentlyHandler(
            _repository,
            new DeleteConsumerPermanentlyValidator());
    }

    [Fact]
    public async Task HandleAsync_RevokedConsumer_DeletesConsumer()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "retired-team", "hash");
        consumer.Revoke();
        _repository.GetByNameAsync(consumer.Name, Arg.Any<CancellationToken>()).Returns(consumer);

        await _handler.HandleAsync(new DeleteConsumerPermanentlyCommand(consumer.Name));

        await _repository.Received(1).DeleteAsync(consumer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ActiveConsumer_ThrowsConflictWithoutDeleting()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "active-team", "hash");
        _repository.GetByNameAsync(consumer.Name, Arg.Any<CancellationToken>()).Returns(consumer);

        var act = () => _handler.HandleAsync(new DeleteConsumerPermanentlyCommand(consumer.Name));

        await act.Should().ThrowAsync<ConflictException>();
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<Consumer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_UnknownConsumer_ThrowsNotFound()
    {
        _repository.GetByNameAsync("missing", Arg.Any<CancellationToken>()).Returns((Consumer?)null);

        var act = () => _handler.HandleAsync(new DeleteConsumerPermanentlyCommand("missing"));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}