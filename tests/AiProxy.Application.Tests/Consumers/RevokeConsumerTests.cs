using AiProxy.Application.Abstractions;
using AiProxy.Application.Consumers;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Consumers;

public class RevokeConsumerTests
{
    private readonly IConsumerRepository _repository = Substitute.For<IConsumerRepository>();
    private readonly RevokeConsumerHandler _handler;

    public RevokeConsumerTests()
    {
        _handler = new RevokeConsumerHandler(_repository, new RevokeConsumerValidator());
    }

    [Fact]
    public async Task HandleAsync_WithExistingConsumer_RevokesAndPersists()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash");
        _repository.GetByNameAsync("team-checkout", Arg.Any<CancellationToken>()).Returns(consumer);

        var result = await _handler.HandleAsync(new RevokeConsumerCommand("team-checkout"));

        result.Enabled.Should().BeFalse();
        result.RevokedAt.Should().NotBeNull();
        await _repository.Received(1).UpdateAsync(consumer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithUnknownConsumer_ThrowsNotFoundException()
    {
        _repository.GetByNameAsync("missing", Arg.Any<CancellationToken>()).Returns((Consumer?)null);

        var act = () => _handler.HandleAsync(new RevokeConsumerCommand("missing"));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
