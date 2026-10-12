using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Application.Models;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AIProxyPlus.Application.Tests.Models;

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
