using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.Models;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Models;

public class RemoveEndpointTests
{
    private readonly IModelRepository _repository = Substitute.For<IModelRepository>();
    private readonly RemoveEndpointHandler _handler;

    public RemoveEndpointTests()
    {
        _handler = new RemoveEndpointHandler(_repository, new RemoveEndpointValidator());
    }

    [Fact]
    public async Task HandleAsync_WithExistingEndpoint_RemovesItAndPersists()
    {
        var model = Model.Create(Guid.NewGuid(), "gpt-4o-prod");
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "OpenAI primary", "https://api.openai.com", ApiFamily.OpenAI, AuthMode.ApiKey, secretRef: "x");
        model.AddEndpoint(ModelEndpoint.Create(model.Id, endpoint, 1));
        _repository.GetByNameAsync(model.Name, Arg.Any<CancellationToken>()).Returns(model);

        await _handler.HandleAsync(new RemoveEndpointCommand(model.Name, endpoint.Id));

        model.Endpoints.Should().BeEmpty();
        await _repository.Received(1).UpdateAsync(model, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithUnknownEndpoint_ThrowsNotFoundException()
    {
        var model = Model.Create(Guid.NewGuid(), "gpt-4o-prod");
        _repository.GetByNameAsync(model.Name, Arg.Any<CancellationToken>()).Returns(model);

        var act = () => _handler.HandleAsync(new RemoveEndpointCommand(model.Name, Guid.NewGuid()));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_WithUnknownModel_ThrowsNotFoundException()
    {
        _repository.GetByNameAsync("missing", Arg.Any<CancellationToken>()).Returns((Model?)null);

        var act = () => _handler.HandleAsync(new RemoveEndpointCommand("missing", Guid.NewGuid()));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
