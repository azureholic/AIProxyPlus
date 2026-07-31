using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.ModelDeployments;
using AiProxy.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.ModelDeployments;

public class DeleteModelDeploymentTests
{
    private readonly IModelDeploymentRepository _repository = Substitute.For<IModelDeploymentRepository>();
    private readonly DeleteModelDeploymentHandler _handler;

    public DeleteModelDeploymentTests()
    {
        _handler = new DeleteModelDeploymentHandler(_repository, new DeleteModelDeploymentValidator());
    }

    [Fact]
    public async Task HandleAsync_WithExistingDeployment_DeletesById()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        _repository.GetByNameAsync("gpt-4o-prod", Arg.Any<CancellationToken>()).Returns(deployment);

        await _handler.HandleAsync(new DeleteModelDeploymentCommand("gpt-4o-prod"));

        await _repository.Received(1).DeleteAsync(deployment.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithUnknownDeployment_ThrowsNotFoundException()
    {
        _repository.GetByNameAsync("missing", Arg.Any<CancellationToken>()).Returns((ModelDeployment?)null);

        var act = () => _handler.HandleAsync(new DeleteModelDeploymentCommand("missing"));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
