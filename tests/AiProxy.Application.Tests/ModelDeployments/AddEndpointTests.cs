using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.ModelDeployments;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace AiProxy.Application.Tests.ModelDeployments;

public class AddEndpointTests
{
    private readonly IModelDeploymentRepository _repository = Substitute.For<IModelDeploymentRepository>();
    private readonly ISecretResolver _secretResolver = Substitute.For<ISecretResolver>();
    private readonly AddEndpointHandler _handler;

    public AddEndpointTests()
    {
        _handler = new AddEndpointHandler(_repository, _secretResolver, new AddEndpointValidator());
    }

    private ModelDeployment GivenDeployment(string name = "gpt-4o-prod")
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), name);
        _repository.GetByNameAsync(name, Arg.Any<CancellationToken>()).Returns(deployment);
        return deployment;
    }

    [Fact]
    public async Task HandleAsync_WithApiKeyAuth_StoresSecretAndAddsEndpoint()
    {
        var deployment = GivenDeployment();

        var endpoint = await _handler.HandleAsync(new AddEndpointCommand(
            deployment.Name, "https://api.openai.com", ApiFamily.OpenAI, 1, AuthMode.ApiKey, ApiKeyPlaintext: "sk-abc"));

        endpoint.SecretRef.Should().Be($"endpoints/{endpoint.Id}");
        await _secretResolver.Received(1).StoreAsync(endpoint.SecretRef!, "sk-abc", Arg.Any<CancellationToken>());
        deployment.Endpoints.Should().ContainSingle(e => e.Id == endpoint.Id);
    }

    [Fact]
    public async Task HandleAsync_WithManagedIdentityAuth_DoesNotTouchSecretResolver()
    {
        GivenDeployment();

        var endpoint = await _handler.HandleAsync(new AddEndpointCommand(
            "gpt-4o-prod", "https://foundry.example.com", ApiFamily.AzureOpenAI, 1, AuthMode.ManagedIdentity,
            ManagedIdentityScope: "https://cognitiveservices.azure.com/.default"));

        endpoint.SecretRef.Should().BeNull();
        await _secretResolver.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default);
    }

    [Fact]
    public async Task HandleAsync_WithHttpNonLocalhostUrl_ThrowsValidationException()
    {
        GivenDeployment();

        var act = () => _handler.HandleAsync(new AddEndpointCommand(
            "gpt-4o-prod", "http://example.com", ApiFamily.OpenAI, 1, AuthMode.ApiKey, ApiKeyPlaintext: "sk-abc"));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task HandleAsync_WithApiKeyAuthAndNoPlaintext_ThrowsValidationException()
    {
        GivenDeployment();

        var act = () => _handler.HandleAsync(new AddEndpointCommand(
            "gpt-4o-prod", "https://api.openai.com", ApiFamily.OpenAI, 1, AuthMode.ApiKey));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task HandleAsync_WithUnknownDeployment_ThrowsNotFoundException()
    {
        _repository.GetByNameAsync("missing", Arg.Any<CancellationToken>()).Returns((ModelDeployment?)null);

        var act = () => _handler.HandleAsync(new AddEndpointCommand(
            "missing", "https://api.openai.com", ApiFamily.OpenAI, 1, AuthMode.ApiKey, ApiKeyPlaintext: "sk-abc"));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HandleAsync_WithDuplicatePriority_IsAllowed_ForRoundRobin()
    {
        var deployment = GivenDeployment();
        deployment.AddEndpoint(Endpoint.Create(
            Guid.NewGuid(), deployment.Id, "https://api.openai.com", ApiFamily.OpenAI, 1, AuthMode.ApiKey, secretRef: "x"));

        var endpoint = await _handler.HandleAsync(new AddEndpointCommand(
            deployment.Name, "https://api.openai.com", ApiFamily.OpenAI, 1, AuthMode.ApiKey, ApiKeyPlaintext: "sk-abc"));

        endpoint.Priority.Should().Be(1);
        deployment.Endpoints.Should().HaveCount(2);
    }
}
