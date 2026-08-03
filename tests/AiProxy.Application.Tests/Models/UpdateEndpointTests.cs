using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.Models;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Models;

public class UpdateEndpointTests
{
    private readonly IModelRepository _repository = Substitute.For<IModelRepository>();
    private readonly UpdateEndpointHandler _handler;

    public UpdateEndpointTests()
    {
        _handler = new UpdateEndpointHandler(_repository, new UpdateEndpointValidator());
    }

    private (Model, ModelEndpoint Assignment) GivenModelWithEndpoint()
    {
        var model = Model.Create(Guid.NewGuid(), "gpt-4o-prod");
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "OpenAI primary", "https://api.openai.com", ApiFamily.OpenAI, AuthMode.ApiKey, secretRef: "x");
        var assignment = ModelEndpoint.Create(model.Id, endpoint, 1);
        model.AddEndpoint(assignment);
        _repository.GetByNameAsync(model.Name, Arg.Any<CancellationToken>()).Returns(model);
        return (model, assignment);
    }

    [Fact]
    public async Task HandleAsync_WithEditableFields_UpdatesAssignment()
    {
        var (model, endpoint) = GivenModelWithEndpoint();

        var result = await _handler.HandleAsync(new UpdateEndpointCommand(model.Name, endpoint.Id, Priority: 3, Enabled: false));

        result.Priority.Should().Be(3);
        result.Enabled.Should().BeFalse();
        await _repository.Received(1).UpdateAsync(model, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithNoFieldsSet_LeavesEndpointUnchanged()
    {
        var (model, endpoint) = GivenModelWithEndpoint();

        var result = await _handler.HandleAsync(new UpdateEndpointCommand(model.Name, endpoint.Id));

        result.Priority.Should().Be(1);
        result.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithUnknownEndpoint_ThrowsNotFoundException()
    {
        var (model, _) = GivenModelWithEndpoint();

        var act = () => _handler.HandleAsync(new UpdateEndpointCommand(model.Name, Guid.NewGuid(), Priority: 2));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_ToPriorityUsedByAnotherEndpoint_IsAllowed_ForRoundRobin()
    {
        var (model, endpoint) = GivenModelWithEndpoint();
        var otherEndpoint = Endpoint.Create(
            Guid.NewGuid(), "OpenAI secondary", "https://api.openai.com", ApiFamily.OpenAI, AuthMode.ApiKey, secretRef: "y");
        model.AddEndpoint(ModelEndpoint.Create(model.Id, otherEndpoint, 2));

        var result = await _handler.HandleAsync(new UpdateEndpointCommand(model.Name, endpoint.Id, Priority: 2));

        result.Priority.Should().Be(2);
    }

}
