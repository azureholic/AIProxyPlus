using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Endpoints;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace AIProxyPlus.Application.Tests.Endpoints;

public class EndpointLifecycleTests
{
    private readonly IEndpointRepository _repository = Substitute.For<IEndpointRepository>();
    private readonly ISecretResolver _secretResolver = Substitute.For<ISecretResolver>();
    private readonly IEndpointCredentialCache _credentialCache = Substitute.For<IEndpointCredentialCache>();
    private readonly IEndpointCredentialInvalidationPublisher _invalidationPublisher =
        Substitute.For<IEndpointCredentialInvalidationPublisher>();
    private readonly IEndpointCredentialRetirementQueue _retirementQueue =
        Substitute.For<IEndpointCredentialRetirementQueue>();

    [Fact]
    public async Task CreateAsync_WithApiKey_StoresSecretAndPersistsEndpoint()
    {
        var handler = new CreateEndpointHandler(_repository, _secretResolver, new CreateEndpointValidator());

        var endpoint = await handler.HandleAsync(new CreateEndpointCommand(
            "OpenAI primary", "https://api.openai.com", AuthMode.ApiKey, "sk-test"));

        endpoint.Name.Should().Be("OpenAI primary");
        endpoint.SecretRef.Should().StartWith($"endpoints/{endpoint.Id}/primary/");
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

    [Theory]
    [InlineData(0)]
    [InlineData(Endpoint.MaxTimeoutSeconds + 1)]
    [InlineData(int.MaxValue)]
    public async Task CreateAsync_WithTimeoutSecondsOutOfRange_ThrowsValidationException(int timeoutSeconds)
    {
        var handler = new CreateEndpointHandler(_repository, _secretResolver, new CreateEndpointValidator());

        var act = () => handler.HandleAsync(new CreateEndpointCommand(
            "OpenAI primary", "https://api.openai.com", AuthMode.ApiKey, "sk-test", TimeoutSeconds: timeoutSeconds));

        await act.Should().ThrowAsync<ValidationException>();
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Endpoint.MaxTimeoutSeconds + 1)]
    [InlineData(int.MaxValue)]
    public async Task UpdateAsync_WithTimeoutSecondsOutOfRange_ThrowsValidationException(int timeoutSeconds)
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "Foundry primary", "https://old.example.com", AuthMode.ManagedIdentity,
            managedIdentityScope: "https://cognitiveservices.azure.com/.default");
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        var handler = new UpdateEndpointConfigurationHandler(_repository, new UpdateEndpointConfigurationValidator());

        var act = () => handler.HandleAsync(new UpdateEndpointConfigurationCommand(
            endpoint.Id, "Foundry shared", "https://new.example.com", "https://ai.azure.com/.default",
            timeoutSeconds, 4, "72f988bf-86f1-41af-91ab-2d7cd011db47"));

        await act.Should().ThrowAsync<ValidationException>();
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
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
        var handler = new DeleteEndpointHandler(
            _repository, _credentialCache, _invalidationPublisher, _retirementQueue, new DeleteEndpointValidator());

        var act = () => handler.HandleAsync(new DeleteEndpointCommand(endpoint.Id));

        await act.Should().ThrowAsync<ConflictException>();
        await _repository.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
        _retirementQueue.DidNotReceiveWithAnyArgs().ScheduleRetirement(default, default!);
    }

    [Fact]
    public async Task DeleteAsync_WhenEndpointIsUnassigned_DeletesEndpointAndSchedulesCredentialRetirement()
    {
        var endpoint = CreateApiKeyEndpoint();
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        _repository.IsAssignedAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new DeleteEndpointHandler(
            _repository, _credentialCache, _invalidationPublisher, _retirementQueue, new DeleteEndpointValidator());

        await handler.HandleAsync(new DeleteEndpointCommand(endpoint.Id));

        await _repository.Received(1).DeleteAsync(endpoint.Id, Arg.Any<CancellationToken>());
        _credentialCache.Received(1).Invalidate(endpoint.SecretRef!);
        _retirementQueue.Received(1).ScheduleRetirement(endpoint.Id, endpoint.SecretRef!);
        await _invalidationPublisher.Received(1).PublishAsync(
            Arg.Is<EndpointCredentialInvalidation>(message =>
                message.EndpointId == endpoint.Id && message.SecretRefs.SequenceEqual(new[] { endpoint.SecretRef! })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetSecretAsync_WithManagedIdentityEndpoint_ThrowsConflictException()
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "Foundry primary", "https://foundry.example.com", AuthMode.ManagedIdentity,
            managedIdentityScope: "https://ai.azure.com/.default");
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        var handler = new SetEndpointSecretHandler(
            _repository, _secretResolver, _credentialCache, _invalidationPublisher, _retirementQueue, new SetEndpointSecretValidator());

        var act = () => handler.HandleAsync(new SetEndpointSecretCommand(endpoint.Id, "sk-new"));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task RemoveCredentialAsync_WithInactiveSecondary_SchedulesRemovedReferenceForRetirement()
    {
        var endpoint = CreateApiKeyEndpoint();
        const string secondaryRef = "endpoints/secondary/version-1";
        endpoint.SetSecretReference(EndpointCredentialSlot.Secondary, secondaryRef);
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        var handler = new RemoveEndpointCredentialHandler(
            _repository, _invalidationPublisher, _retirementQueue, TimeProvider.System);

        await handler.HandleAsync(new RemoveEndpointCredentialCommand(endpoint.Id, EndpointCredentialSlot.Secondary));

        _retirementQueue.Received(1).ScheduleRetirement(endpoint.Id, secondaryRef);
    }

    [Fact]
    public async Task SetSecretAsync_WithApiKeyEndpoint_InvalidatesCachedCredential()
    {
        var endpoint = CreateApiKeyEndpoint();
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        var handler = new SetEndpointSecretHandler(
            _repository, _secretResolver, _credentialCache, _invalidationPublisher, _retirementQueue, new SetEndpointSecretValidator());

        await handler.HandleAsync(new SetEndpointSecretCommand(endpoint.Id, "sk-new"));

        await _secretResolver.Received(1).StoreAsync(endpoint.SecretRef!, "sk-new", Arg.Any<CancellationToken>());
        _credentialCache.Received(1).Invalidate(endpoint.SecretRef!);
    }

    [Fact]
    public async Task SetCredentialAsync_WithSecondarySlot_WritesImmutableReferenceBeforeUpdatingEndpoint()
    {
        var endpoint = CreateApiKeyEndpoint();
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        var handler = new SetEndpointCredentialHandler(
            _repository, _secretResolver, _credentialCache, _invalidationPublisher, _retirementQueue, new SetEndpointCredentialValidator());

        var result = await handler.HandleAsync(
            new SetEndpointCredentialCommand(endpoint.Id, EndpointCredentialSlot.Secondary, "sk-secondary"));

        result.SecondarySecretRef.Should().StartWith($"endpoints/{endpoint.Id}/secondary/");
        await _secretResolver.Received(1).StoreAsync(
            result.SecondarySecretRef!, "sk-secondary", Arg.Any<CancellationToken>());
        await _repository.Received(1).UpdateAsync(endpoint, Arg.Any<CancellationToken>());
        await _invalidationPublisher.Received(1).PublishAsync(
            Arg.Is<EndpointCredentialInvalidation>(message =>
                message.EndpointId == endpoint.Id && message.SecretRefs.Contains(result.SecondarySecretRef!)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCredentialAsync_WhenPersistenceFails_DeletesNewUnreferencedSecret()
    {
        var endpoint = CreateApiKeyEndpoint();
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        _repository.IsSecretReferenceInUseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _repository.UpdateAsync(endpoint, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Database unavailable"));
        var handler = new SetEndpointCredentialHandler(
            _repository, _secretResolver, _credentialCache, _invalidationPublisher, _retirementQueue, new SetEndpointCredentialValidator());

        var act = () => handler.HandleAsync(
            new SetEndpointCredentialCommand(endpoint.Id, EndpointCredentialSlot.Secondary, "sk-secondary"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _secretResolver.Received(1).DeleteAsync(
            Arg.Is<string>(secretRef => secretRef.StartsWith($"endpoints/{endpoint.Id}/secondary/")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCredentialAsync_WhenPersistenceThrowsAfterCommit_ReconcilesAsSuccess()
    {
        var endpoint = CreateApiKeyEndpoint();
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        _repository.IsSecretReferenceInUseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _repository.UpdateAsync(endpoint, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Commit outcome unknown"));
        var handler = new SetEndpointCredentialHandler(
            _repository, _secretResolver, _credentialCache, _invalidationPublisher, _retirementQueue, new SetEndpointCredentialValidator());

        var result = await handler.HandleAsync(
            new SetEndpointCredentialCommand(endpoint.Id, EndpointCredentialSlot.Secondary, "sk-secondary"));

        result.Should().BeSameAs(endpoint);
        await _secretResolver.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
        _credentialCache.Received(1).Invalidate(endpoint.SecondarySecretRef!);
        await _invalidationPublisher.Received(1).PublishAsync(
            Arg.Is<EndpointCredentialInvalidation>(message =>
                message.EndpointId == endpoint.Id && message.SecretRefs.SequenceEqual(new[] { endpoint.SecondarySecretRef! })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActivateCredentialAsync_WhenFallbackExceedsConfiguredMaximum_ThrowsValidationException()
    {
        var now = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        var endpoint = CreateApiKeyEndpoint();
        endpoint.SetSecretReference(EndpointCredentialSlot.Secondary, "endpoints/secondary");
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        var handler = new ActivateEndpointCredentialHandler(
            _repository,
            _invalidationPublisher,
            new FixedTimeProvider(now),
            new EndpointCredentialOptions { MaxCredentialFallbackWindow = TimeSpan.FromHours(1) });

        var act = () => handler.HandleAsync(new ActivateEndpointCredentialCommand(
            endpoint.Id, EndpointCredentialSlot.Secondary, now.AddHours(2)));

        await act.Should().ThrowAsync<ValidationException>();
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task RemoveCredentialAsync_WhenSecondaryIsFallback_ThrowsConflictException()
    {
        var now = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        var endpoint = CreateApiKeyEndpoint();
        endpoint.SetSecretReference(EndpointCredentialSlot.Secondary, "endpoints/secondary");
        endpoint.ActivateCredential(EndpointCredentialSlot.Secondary, null, now);
        endpoint.ActivateCredential(EndpointCredentialSlot.Primary, now.AddMinutes(10), now);
        _repository.GetByIdAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        var handler = new RemoveEndpointCredentialHandler(
            _repository, _invalidationPublisher, _retirementQueue, new FixedTimeProvider(now));

        var act = () => handler.HandleAsync(
            new RemoveEndpointCredentialCommand(endpoint.Id, EndpointCredentialSlot.Secondary));

        await act.Should().ThrowAsync<ConflictException>();
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    private static Endpoint CreateApiKeyEndpoint() => Endpoint.Create(
        Guid.NewGuid(), "OpenAI primary", "https://api.openai.com", AuthMode.ApiKey, secretRef: "endpoints/abc");

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
