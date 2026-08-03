using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.Models;
using AiProxy.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Models;

public class DeleteModelTests
{
    private readonly IModelRepository _repository = Substitute.For<IModelRepository>();
    private readonly DeleteModelHandler _handler;

    public DeleteModelTests()
    {
        _handler = new DeleteModelHandler(_repository, new DeleteModelValidator());
    }

    [Fact]
    public async Task HandleAsync_WithExistingModel_DeletesById()
    {
        var model = Model.Create(Guid.NewGuid(), "gpt-4o-prod");
        _repository.GetByNameAsync("gpt-4o-prod", Arg.Any<CancellationToken>()).Returns(model);

        await _handler.HandleAsync(new DeleteModelCommand("gpt-4o-prod"));

        await _repository.Received(1).DeleteAsync(model.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithUnknownModel_ThrowsNotFoundException()
    {
        _repository.GetByNameAsync("missing", Arg.Any<CancellationToken>()).Returns((Model?)null);

        var act = () => _handler.HandleAsync(new DeleteModelCommand("missing"));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
