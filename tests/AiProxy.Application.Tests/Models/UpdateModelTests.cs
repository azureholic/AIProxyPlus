using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.Models;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Models;

public class UpdateModelTests
{
    private readonly IModelRepository _repository = Substitute.For<IModelRepository>();
    private readonly UpdateModelHandler _handler;

    public UpdateModelTests()
    {
        _handler = new UpdateModelHandler(_repository, new UpdateModelValidator());
    }

    [Fact]
    public async Task HandleAsync_WithExistingModel_UpdatesPolicyFields()
    {
        var model = Model.Create(Guid.NewGuid(), "gpt-4o-prod");
        _repository.GetByNameAsync("gpt-4o-prod", Arg.Any<CancellationToken>()).Returns(model);

        var result = await _handler.HandleAsync(new UpdateModelCommand("gpt-4o-prod", RoutingStrategy.Priority));

        result.Strategy.Should().Be(RoutingStrategy.Priority);
        await _repository.Received(1).UpdateAsync(model, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithUnknownModel_ThrowsNotFoundException()
    {
        _repository.GetByNameAsync("missing", Arg.Any<CancellationToken>()).Returns((Model?)null);

        var act = () => _handler.HandleAsync(new UpdateModelCommand("missing", RoutingStrategy.Priority));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
