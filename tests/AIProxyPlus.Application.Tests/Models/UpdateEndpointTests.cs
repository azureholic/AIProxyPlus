using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Application.Models;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AIProxyPlus.Application.Tests.Models;

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
            Guid.NewGuid(), "OpenAI primary", "https://api.openai.com", AuthMode.ApiKey, secretRef: "x");
        var route = ApiRoute.Create(Guid.NewGuid(), "Native OpenAI", Provider.OpenAI, "/v1");
        var assignment = ModelEndpoint.Create(Guid.NewGuid(), model.Id, endpoint, route, 1);
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
            Guid.NewGuid(), "OpenAI secondary", "https://api.openai.com", AuthMode.ApiKey, secretRef: "y");
        var route = ApiRoute.Create(Guid.NewGuid(), "Native OpenAI 2", Provider.OpenAI, "/v2");
        model.AddEndpoint(ModelEndpoint.Create(Guid.NewGuid(), model.Id, otherEndpoint, route, 2));

        var result = await _handler.HandleAsync(new UpdateEndpointCommand(model.Name, endpoint.Id, Priority: 2));

        result.Priority.Should().Be(2);
    }

}
