using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.ModelDeployments;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.ModelDeployments;

public class UpdateEndpointTests
{
    private readonly IModelDeploymentRepository _repository = Substitute.For<IModelDeploymentRepository>();
    private readonly UpdateEndpointHandler _handler;

    public UpdateEndpointTests()
    {
        _handler = new UpdateEndpointHandler(_repository, new UpdateEndpointValidator());
    }

    private (ModelDeployment Deployment, ModelDeploymentEndpoint Assignment) GivenDeploymentWithEndpoint()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "OpenAI primary", "https://api.openai.com", ApiFamily.OpenAI, AuthMode.ApiKey, secretRef: "x");
        var assignment = ModelDeploymentEndpoint.Create(deployment.Id, endpoint, 1);
        deployment.AddEndpoint(assignment);
        _repository.GetByNameAsync(deployment.Name, Arg.Any<CancellationToken>()).Returns(deployment);
        return (deployment, assignment);
    }

    [Fact]
    public async Task HandleAsync_WithEditableFields_UpdatesAssignment()
    {
        var (deployment, endpoint) = GivenDeploymentWithEndpoint();

        var result = await _handler.HandleAsync(new UpdateEndpointCommand(deployment.Name, endpoint.Id, Priority: 3, Enabled: false));

        result.Priority.Should().Be(3);
        result.Enabled.Should().BeFalse();
        await _repository.Received(1).UpdateAsync(deployment, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithNoFieldsSet_LeavesEndpointUnchanged()
    {
        var (deployment, endpoint) = GivenDeploymentWithEndpoint();

        var result = await _handler.HandleAsync(new UpdateEndpointCommand(deployment.Name, endpoint.Id));

        result.Priority.Should().Be(1);
        result.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithUnknownEndpoint_ThrowsNotFoundException()
    {
        var (deployment, _) = GivenDeploymentWithEndpoint();

        var act = () => _handler.HandleAsync(new UpdateEndpointCommand(deployment.Name, Guid.NewGuid(), Priority: 2));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_ToPriorityUsedByAnotherEndpoint_IsAllowed_ForRoundRobin()
    {
        var (deployment, endpoint) = GivenDeploymentWithEndpoint();
        var otherEndpoint = Endpoint.Create(
            Guid.NewGuid(), "OpenAI secondary", "https://api.openai.com", ApiFamily.OpenAI, AuthMode.ApiKey, secretRef: "y");
        deployment.AddEndpoint(ModelDeploymentEndpoint.Create(deployment.Id, otherEndpoint, 2));

        var result = await _handler.HandleAsync(new UpdateEndpointCommand(deployment.Name, endpoint.Id, Priority: 2));

        result.Priority.Should().Be(2);
    }

}
