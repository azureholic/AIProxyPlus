using AiProxy.Application.Abstractions;
using AiProxy.Application.Endpoints;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace AiProxy.Application.Tests.Endpoints;

public class EndpointLifecycleTests
{
    private readonly IEndpointRepository _repository = Substitute.For<IEndpointRepository>();
    private readonly ISecretResolver _secretResolver = Substitute.For<ISecretResolver>();

    [Fact]
    public async Task CreateAsync_WithApiKey_StoresSecretAndPersistsEndpoint()
    {
        var handler = new CreateEndpointHandler(_repository, _secretResolver, new CreateEndpointValidator());

        var endpoint = await handler.HandleAsync(new CreateEndpointCommand(
            "OpenAI primary", "https://api.openai.com", AuthMode.ApiKey, "sk-test"));

        endpoint.Name.Should().Be("OpenAI primary");
        endpoint.SecretRef.Should().Be($"endpoints/{endpoint.Id}");
        await _secretResolver.Received(1).StoreAsync(endpoint.SecretRef, "sk-test", Arg.Any<CancellationToken>());
        await _repository.Received(1).AddAsync(endpoint, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WhenPersistenceFails_DeletesStoredSecret()
    {
        _repository.AddAsync(Arg.Any<Endpoint>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Database unavailable"));
        var handler = new CreateEndpointHandler(_repository, _secretResolver, new CreateEndpointValidator());

        var act = () => handler.HandleAsync(new CreateEndpointCommand(
            "OpenAI primary", "https://api.openai.com", AuthMode.ApiKey, "sk-test"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _secretResolver.Received(1).DeleteAsync(
            Arg.Is<string>(secretRef => secretRef.StartsWith("endpoints/")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateName_ThrowsConflictException()
    {
        _repository.ExistsByNameAsync("OpenAI primary", null, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateEndpointHandler(_repository, _secretResolver, new CreateEndpointValidator());

        var act = () => handler.HandleAsync(new CreateEndpointCommand(
            "OpenAI primary", "https://api.openai.com", AuthMode.ApiKey, "sk-test"));

        await act.Should().ThrowAsync<ConflictException>();
        await _secretResolver.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default);
    }

    [Fact]
    public async Task CreateAsync_WithHttpNonLocalhostUrl_ThrowsValidationException()
    {
        var handler = new CreateEndpointHandler(_repository, _secretResolver, new CreateEndpointValidator());

        var act = () => handler.HandleAsync(new CreateEndpointCommand(
            "OpenAI primary", "http://example.com", AuthMode.ApiKey, "sk-test"));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task UpdateAsync_WithManagedIdentitySettings_UpdatesReusableEndpoint()
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "Foundry primary", "https://old.example.com", AuthMode.ManagedIdentity,
            managedIdentityScope: "https://cognitiveservices.azure.com/.default");
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        var handler = new UpdateEndpointConfigurationHandler(_repository, new UpdateEndpointConfigurationValidator());

        var result = await handler.HandleAsync(new UpdateEndpointConfigurationCommand(
            endpoint.Id,
            "Foundry shared",
            "https://new.example.com",
            "https://ai.azure.com/.default",
            30,
            4,
            "72f988bf-86f1-41af-91ab-2d7cd011db47"));

        result.Name.Should().Be("Foundry shared");
        result.BaseUrl.Should().Be("https://new.example.com");
        result.ManagedIdentityScope.Should().Be("https://ai.azure.com/.default");
        result.ManagedIdentityTenantId.Should().Be("72f988bf-86f1-41af-91ab-2d7cd011db47");
        result.TimeoutSeconds.Should().Be(30);
        result.MaxRetries.Should().Be(4);
        await _repository.Received(1).UpdateAsync(endpoint, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_WhenEndpointIsAssigned_ThrowsConflictException()
    {
        var endpoint = CreateApiKeyEndpoint();
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        _repository.IsAssignedAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new DeleteEndpointHandler(_repository, _secretResolver, new DeleteEndpointValidator());

        var act = () => handler.HandleAsync(new DeleteEndpointCommand(endpoint.Id));

        await act.Should().ThrowAsync<ConflictException>();
        await _repository.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
        await _secretResolver.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task DeleteAsync_WhenEndpointIsUnassigned_DeletesEndpointAndSecret()
    {
        var endpoint = CreateApiKeyEndpoint();
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        _repository.IsAssignedAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new DeleteEndpointHandler(_repository, _secretResolver, new DeleteEndpointValidator());

        await handler.HandleAsync(new DeleteEndpointCommand(endpoint.Id));

        await _repository.Received(1).DeleteAsync(endpoint.Id, Arg.Any<CancellationToken>());
        await _secretResolver.Received(1).DeleteAsync(endpoint.SecretRef!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetSecretAsync_WithManagedIdentityEndpoint_ThrowsConflictException()
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "Foundry primary", "https://foundry.example.com", AuthMode.ManagedIdentity,
            managedIdentityScope: "https://ai.azure.com/.default");
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        var handler = new SetEndpointSecretHandler(_repository, _secretResolver, new SetEndpointSecretValidator());

        var act = () => handler.HandleAsync(new SetEndpointSecretCommand(endpoint.Id, "sk-new"));

        await act.Should().ThrowAsync<ConflictException>();
    }

    private static Endpoint CreateApiKeyEndpoint() => Endpoint.Create(
        Guid.NewGuid(), "OpenAI primary", "https://api.openai.com", AuthMode.ApiKey, secretRef: "endpoints/abc");
}
