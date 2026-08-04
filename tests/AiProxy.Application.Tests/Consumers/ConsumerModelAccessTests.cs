using AiProxy.Application.Abstractions;
using AiProxy.Application.Consumers;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Consumers;

public class ConsumerModelAccessTests
{
    private readonly IConsumerRepository _consumers = Substitute.For<IConsumerRepository>();
    private readonly IModelRepository _models = Substitute.For<IModelRepository>();

    [Fact]
    public async Task AllowAsync_WithExistingConsumerAndModel_PersistsRestriction()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash");
        var model = Model.Create(Guid.NewGuid(), "gpt-4o");
        _consumers.GetByNameAsync(consumer.Name, Arg.Any<CancellationToken>()).Returns(consumer);
        _models.GetByNameAsync(model.Name, Arg.Any<CancellationToken>()).Returns(model);
        var handler = new AllowConsumerModelHandler(_consumers, _models, new AllowConsumerModelValidator());

        var result = await handler.HandleAsync(new AllowConsumerModelCommand(consumer.Name, model.Name));

        result.AllowedModelIds.Should().ContainSingle().Which.Should().Be(model.Id);
        await _consumers.Received(1).UpdateAsync(consumer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_RemovingLastModel_ReturnsConsumerToUnrestrictedAccess()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash");
        var model = Model.Create(Guid.NewGuid(), "gpt-4o");
        consumer.AllowModel(model.Id);
        _consumers.GetByNameAsync(consumer.Name, Arg.Any<CancellationToken>()).Returns(consumer);
        _models.GetByNameAsync(model.Name, Arg.Any<CancellationToken>()).Returns(model);
        var handler = new RevokeConsumerModelHandler(_consumers, _models, new RevokeConsumerModelValidator());

        var result = await handler.HandleAsync(new RevokeConsumerModelCommand(consumer.Name, model.Name));

        result.HasUnrestrictedModelAccess.Should().BeTrue();
        await _consumers.Received(1).UpdateAsync(consumer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllowAsync_WithUnknownModel_ThrowsNotFoundWithoutUpdatingConsumer()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash");
        _consumers.GetByNameAsync(consumer.Name, Arg.Any<CancellationToken>()).Returns(consumer);
        _models.GetByNameAsync("missing", Arg.Any<CancellationToken>()).Returns((Model?)null);
        var handler = new AllowConsumerModelHandler(_consumers, _models, new AllowConsumerModelValidator());

        var act = () => handler.HandleAsync(new AllowConsumerModelCommand(consumer.Name, "missing"));

        await act.Should().ThrowAsync<NotFoundException>();
        await _consumers.DidNotReceive().UpdateAsync(Arg.Any<Consumer>(), Arg.Any<CancellationToken>());
    }
}
