using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.ModelDeployments;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.ModelDeployments;

public class SetEndpointSecretTests
{
    private readonly IModelDeploymentRepository _repository = Substitute.For<IModelDeploymentRepository>();
    private readonly ISecretResolver _secretResolver = Substitute.For<ISecretResolver>();
    private readonly SetEndpointSecretHandler _handler;

    public SetEndpointSecretTests()
    {
        _handler = new SetEndpointSecretHandler(_repository, _secretResolver, new SetEndpointSecretValidator());
    }

    [Fact]
    public async Task HandleAsync_WithApiKeyEndpoint_StoresNewValueAtExistingRef_WithoutRepositoryWrite()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), deployment.Id, "https://api.openai.com", ApiFamily.OpenAI, 1, AuthMode.ApiKey, secretRef: "endpoints/abc");
        deployment.AddEndpoint(endpoint);
        _repository.GetByNameAsync(deployment.Name, Arg.Any<CancellationToken>()).Returns(deployment);

        await _handler.HandleAsync(new SetEndpointSecretCommand(deployment.Name, endpoint.Id, "sk-new"));

        await _secretResolver.Received(1).StoreAsync("endpoints/abc", "sk-new", Arg.Any<CancellationToken>());
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_WithManagedIdentityEndpoint_ThrowsConflictException()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), deployment.Id, "https://foundry.example.com", ApiFamily.AzureOpenAI, 1,
            AuthMode.ManagedIdentity, managedIdentityScope: "scope");
        deployment.AddEndpoint(endpoint);
        _repository.GetByNameAsync(deployment.Name, Arg.Any<CancellationToken>()).Returns(deployment);

        var act = () => _handler.HandleAsync(new SetEndpointSecretCommand(deployment.Name, endpoint.Id, "sk-new"));

        await act.Should().ThrowAsync<ConflictException>();
    }
}
