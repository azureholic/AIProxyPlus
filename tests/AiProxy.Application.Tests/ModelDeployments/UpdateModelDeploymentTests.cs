using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.ModelDeployments;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.ModelDeployments;

public class UpdateModelDeploymentTests
{
    private readonly IModelDeploymentRepository _repository = Substitute.For<IModelDeploymentRepository>();
    private readonly UpdateModelDeploymentHandler _handler;

    public UpdateModelDeploymentTests()
    {
        _handler = new UpdateModelDeploymentHandler(_repository, new UpdateModelDeploymentValidator());
    }

    [Fact]
    public async Task HandleAsync_WithExistingDeployment_UpdatesPolicyFields()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        _repository.GetByNameAsync("gpt-4o-prod", Arg.Any<CancellationToken>()).Returns(deployment);

        var result = await _handler.HandleAsync(new UpdateModelDeploymentCommand("gpt-4o-prod", RoutingStrategy.Priority));

        result.Strategy.Should().Be(RoutingStrategy.Priority);
        await _repository.Received(1).UpdateAsync(deployment, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithUnknownDeployment_ThrowsNotFoundException()
    {
        _repository.GetByNameAsync("missing", Arg.Any<CancellationToken>()).Returns((ModelDeployment?)null);

        var act = () => _handler.HandleAsync(new UpdateModelDeploymentCommand("missing", RoutingStrategy.Priority));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
