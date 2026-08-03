using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.ModelDeployments;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.ModelDeployments;

public class RemoveEndpointTests
{
    private readonly IModelDeploymentRepository _repository = Substitute.For<IModelDeploymentRepository>();
    private readonly RemoveEndpointHandler _handler;

    public RemoveEndpointTests()
    {
        _handler = new RemoveEndpointHandler(_repository, new RemoveEndpointValidator());
    }

    [Fact]
    public async Task HandleAsync_WithExistingEndpoint_RemovesItAndPersists()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "OpenAI primary", "https://api.openai.com", ApiFamily.OpenAI, AuthMode.ApiKey, secretRef: "x");
        deployment.AddEndpoint(ModelDeploymentEndpoint.Create(deployment.Id, endpoint, 1));
        _repository.GetByNameAsync(deployment.Name, Arg.Any<CancellationToken>()).Returns(deployment);

        await _handler.HandleAsync(new RemoveEndpointCommand(deployment.Name, endpoint.Id));

        deployment.Endpoints.Should().BeEmpty();
        await _repository.Received(1).UpdateAsync(deployment, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithUnknownEndpoint_ThrowsNotFoundException()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        _repository.GetByNameAsync(deployment.Name, Arg.Any<CancellationToken>()).Returns(deployment);

        var act = () => _handler.HandleAsync(new RemoveEndpointCommand(deployment.Name, Guid.NewGuid()));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_WithUnknownDeployment_ThrowsNotFoundException()
    {
        _repository.GetByNameAsync("missing", Arg.Any<CancellationToken>()).Returns((ModelDeployment?)null);

        var act = () => _handler.HandleAsync(new RemoveEndpointCommand("missing", Guid.NewGuid()));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
