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

    private (ModelDeployment Deployment, Endpoint Endpoint) GivenDeploymentWithEndpoint()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), deployment.Id, "https://api.openai.com", ApiFamily.OpenAI, 1, AuthMode.ApiKey, secretRef: "x");
        deployment.AddEndpoint(endpoint);
        _repository.GetByNameAsync(deployment.Name, Arg.Any<CancellationToken>()).Returns(deployment);
        return (deployment, endpoint);
    }

    [Fact]
    public async Task HandleAsync_WithEditableFields_UpdatesEndpoint()
    {
        var (deployment, endpoint) = GivenDeploymentWithEndpoint();

        var result = await _handler.HandleAsync(new UpdateEndpointCommand(
            deployment.Name, endpoint.Id, BaseUrl: "https://updated.openai.azure.com", Priority: 3, Enabled: false,
            TimeoutSeconds: 30, MaxRetries: 5));

        result.BaseUrl.Should().Be("https://updated.openai.azure.com");
        result.Priority.Should().Be(3);
        result.Enabled.Should().BeFalse();
        result.TimeoutSeconds.Should().Be(30);
        result.MaxRetries.Should().Be(5);
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
        deployment.AddEndpoint(Endpoint.Create(
            Guid.NewGuid(), deployment.Id, "https://api.openai.com", ApiFamily.OpenAI, 2, AuthMode.ApiKey, secretRef: "y"));

        var result = await _handler.HandleAsync(new UpdateEndpointCommand(deployment.Name, endpoint.Id, Priority: 2));

        result.Priority.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_WithManagedIdentityScope_UpdatesScope()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-foundry");
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), deployment.Id, "https://foundry.example.com", ApiFamily.AzureOpenAI, 1,
            AuthMode.ManagedIdentity, managedIdentityScope: "https://cognitiveservices.azure.com/.default");
        deployment.AddEndpoint(endpoint);
        _repository.GetByNameAsync(deployment.Name, Arg.Any<CancellationToken>()).Returns(deployment);

        var result = await _handler.HandleAsync(new UpdateEndpointCommand(
            deployment.Name, endpoint.Id, ManagedIdentityScope: "https://ai.azure.com/.default"));

        result.ManagedIdentityScope.Should().Be("https://ai.azure.com/.default");
    }
}
