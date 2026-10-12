using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Application.Models;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AIProxyPlus.Application.Tests.Models;

public class AddEndpointTests
{
    private readonly IModelRepository _repository = Substitute.For<IModelRepository>();
    private readonly IEndpointRepository _endpointRepository = Substitute.For<IEndpointRepository>();
    private readonly IApiRouteRepository _apiRouteRepository = Substitute.For<IApiRouteRepository>();
    private readonly AddEndpointHandler _handler;

    public AddEndpointTests()
    {
        _handler = new AddEndpointHandler(
            _repository, _endpointRepository, _apiRouteRepository, new AddEndpointValidator());
    }

    private Model GivenModel(string name = "gpt-4o-prod")
    {
        var model = Model.Create(Guid.NewGuid(), name);
        _repository.GetByNameAsync(name, Arg.Any<CancellationToken>()).Returns(model);
        return model;
    }

    [Fact]
    public async Task HandleAsync_WithReusableEndpoint_AddsAssignment()
    {
        var model = GivenModel();
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "OpenAI primary", "https://api.openai.com", AuthMode.ApiKey, secretRef: "endpoints/abc");
        var route = ApiRoute.Create(Guid.NewGuid(), "Native OpenAI", Provider.OpenAI, "/v1");
        _endpointRepository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        _apiRouteRepository.GetByIdAsync(route.Id, Arg.Any<CancellationToken>()).Returns(route);

        var assignment = await _handler.HandleAsync(new AddEndpointCommand(model.Name, endpoint.Id, route.Id, 1));

        assignment.Endpoint.Should().BeSameAs(endpoint);
        assignment.ApiRoute.Should().BeSameAs(route);
        assignment.Priority.Should().Be(1);
        model.Endpoints.Should().ContainSingle(e => e.Id == assignment.Id);
        await _repository.Received(1).UpdateAsync(model, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithUnknownModel_ThrowsNotFoundException()
    {
        _repository.GetByNameAsync("missing", Arg.Any<CancellationToken>()).Returns((Model?)null);

        var act = () => _handler.HandleAsync(
            new AddEndpointCommand("missing", Guid.NewGuid(), Guid.NewGuid(), 1));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_WithUnknownEndpoint_ThrowsNotFoundException()
    {
        var model = GivenModel();
        var endpointId = Guid.NewGuid();
        _endpointRepository.GetByIdAsync(endpointId, Arg.Any<CancellationToken>()).Returns((Endpoint?)null);

        var act = () => _handler.HandleAsync(
            new AddEndpointCommand(model.Name, endpointId, Guid.NewGuid(), 1));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_WithUnknownApiRoute_ThrowsNotFoundException()
    {
        var model = GivenModel();
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "OpenAI primary", "https://api.openai.com", AuthMode.ApiKey, secretRef: "x");
        var routeId = Guid.NewGuid();
        _endpointRepository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        _apiRouteRepository.GetByIdAsync(routeId, Arg.Any<CancellationToken>()).Returns((ApiRoute?)null);

        var act = () => _handler.HandleAsync(new AddEndpointCommand(model.Name, endpoint.Id, routeId, 1));

        await act.Should().ThrowAsync<NotFoundException>();
        await _repository.DidNotReceive().UpdateAsync(model, Arg.Any<CancellationToken>());
    }
}
