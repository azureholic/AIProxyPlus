using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.Models;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Models;

public class AddEndpointTests
{
    private readonly IModelRepository _repository = Substitute.For<IModelRepository>();
    private readonly IEndpointRepository _endpointRepository = Substitute.For<IEndpointRepository>();
    private readonly AddEndpointHandler _handler;

    public AddEndpointTests()
    {
        _handler = new AddEndpointHandler(_repository, _endpointRepository, new AddEndpointValidator());
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
            Guid.NewGuid(), "OpenAI primary", "https://api.openai.com", ApiFamily.OpenAI, AuthMode.ApiKey,
            secretRef: "endpoints/abc");
        _endpointRepository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);

        var assignment = await _handler.HandleAsync(new AddEndpointCommand(model.Name, endpoint.Id, 1));

        assignment.Endpoint.Should().BeSameAs(endpoint);
        assignment.Priority.Should().Be(1);
        model.Endpoints.Should().ContainSingle(e => e.Id == endpoint.Id);
        await _repository.Received(1).UpdateAsync(model, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithUnknownModel_ThrowsNotFoundException()
    {
        _repository.GetByNameAsync("missing", Arg.Any<CancellationToken>()).Returns((Model?)null);

        var act = () => _handler.HandleAsync(new AddEndpointCommand("missing", Guid.NewGuid(), 1));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_WithUnknownEndpoint_ThrowsNotFoundException()
    {
        var model = GivenModel();
        var endpointId = Guid.NewGuid();
        _endpointRepository.GetByIdAsync(endpointId, Arg.Any<CancellationToken>()).Returns((Endpoint?)null);

        var act = () => _handler.HandleAsync(new AddEndpointCommand(model.Name, endpointId, 1));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
