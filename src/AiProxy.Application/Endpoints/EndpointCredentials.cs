using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;
using FluentValidation;

namespace AiProxy.Application.Endpoints;

public sealed record SetEndpointCredentialCommand(Guid EndpointId, EndpointCredentialSlot Slot, string ApiKeyPlaintext);
public sealed record SetEndpointSecretCommand(Guid EndpointId, string ApiKeyPlaintext);
public sealed record ActivateEndpointCredentialCommand(
    Guid EndpointId,
    EndpointCredentialSlot ActiveSlot,
    DateTimeOffset? FallbackExpiresAt);
public sealed record DisableEndpointCredentialFallbackCommand(Guid EndpointId);
public sealed record RemoveEndpointCredentialCommand(Guid EndpointId, EndpointCredentialSlot Slot);

public sealed class EndpointCredentialOptions
{
    public const string SectionName = "Endpoints";
    public TimeSpan MaxCredentialFallbackWindow { get; init; } = TimeSpan.FromHours(1);
}

public sealed class SetEndpointCredentialValidator : AbstractValidator<SetEndpointCredentialCommand>
{
    public SetEndpointCredentialValidator()
    {
        RuleFor(command => command.EndpointId).NotEmpty();
        RuleFor(command => command.ApiKeyPlaintext).NotEmpty();
    }
}

public sealed class SetEndpointSecretValidator : AbstractValidator<SetEndpointSecretCommand>
{
    public SetEndpointSecretValidator()
    {
        RuleFor(command => command.EndpointId).NotEmpty();
        RuleFor(command => command.ApiKeyPlaintext).NotEmpty();
    }
}

public sealed class SetEndpointCredentialHandler(
    IEndpointRepository repository,
    ISecretResolver secretResolver,
    IEndpointCredentialCache credentialCache,
    IEndpointCredentialInvalidationPublisher invalidationPublisher,
    IEndpointCredentialRetirementQueue retirementQueue,
    IValidator<SetEndpointCredentialCommand> validator)
{
    public async Task<Endpoint> HandleAsync(SetEndpointCredentialCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var endpoint = await GetApiKeyEndpointAsync(repository, command.EndpointId, cancellationToken);
        var previousRef = command.Slot == EndpointCredentialSlot.Primary
            ? endpoint.PrimarySecretRef
            : endpoint.SecondarySecretRef;
        var slotName = command.Slot.ToString().ToLowerInvariant();
        var newRef = $"endpoints/{endpoint.Id}/{slotName}/{Guid.NewGuid():n}";

        await secretResolver.StoreAsync(newRef, command.ApiKeyPlaintext, cancellationToken);
        try
        {
            endpoint.SetSecretReference(command.Slot, newRef);
            await repository.UpdateAsync(endpoint, cancellationToken);
        }
        catch
        {
            bool? newRefIsPersisted = null;
            try
            {
                newRefIsPersisted = await repository.IsSecretReferenceInUseAsync(newRef, cancellationToken);
            }
            catch
            {
            }

            if (newRefIsPersisted == true)
            {
                credentialCache.Invalidate(newRef);
                try
                {
                    await invalidationPublisher.PublishAsync(
                        new EndpointCredentialInvalidation(endpoint.Id, new[] { newRef }), cancellationToken);
                }
                catch
                {
                }
                if (previousRef is not null)
                {
                    retirementQueue.ScheduleRetirement(endpoint.Id, previousRef);
                }
                return endpoint;
            }
            else if (newRefIsPersisted == false)
            {
                try
                {
                    await secretResolver.DeleteAsync(newRef, cancellationToken);
                }
                catch
                {
                    retirementQueue.ScheduleOrphanCleanup(endpoint.Id, newRef);
                }
            }
            throw;
        }

        if (previousRef is not null)
        {
            credentialCache.Invalidate(previousRef);
        }
        credentialCache.Invalidate(newRef);
        await invalidationPublisher.PublishAsync(
            new EndpointCredentialInvalidation(endpoint.Id, new[] { previousRef, newRef }.OfType<string>().ToArray()),
            cancellationToken);
        if (previousRef is not null)
        {
            retirementQueue.ScheduleRetirement(endpoint.Id, previousRef);
        }
        return endpoint;
    }

    internal static async Task<Endpoint> GetApiKeyEndpointAsync(
        IEndpointRepository repository,
        Guid endpointId,
        CancellationToken cancellationToken)
    {
        var endpoint = await repository.GetByIdAsync(endpointId, cancellationToken)
            ?? throw new NotFoundException($"Endpoint '{endpointId}' was not found.");
        if (endpoint.AuthMode != AuthMode.ApiKey)
        {
            throw new ConflictException("API-key credential slots are available only on ApiKey-mode endpoints.");
        }
        return endpoint;
    }
}

public sealed class SetEndpointSecretHandler(
    IEndpointRepository repository,
    ISecretResolver secretResolver,
    IEndpointCredentialCache credentialCache,
    IEndpointCredentialInvalidationPublisher invalidationPublisher,
    IEndpointCredentialRetirementQueue retirementQueue,
    IValidator<SetEndpointSecretCommand> validator)
{
    public async Task HandleAsync(SetEndpointSecretCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var slotHandler = new SetEndpointCredentialHandler(
            repository, secretResolver, credentialCache, invalidationPublisher, retirementQueue, new SetEndpointCredentialValidator());
        await slotHandler.HandleAsync(
            new SetEndpointCredentialCommand(command.EndpointId, EndpointCredentialSlot.Primary, command.ApiKeyPlaintext),
            cancellationToken);
    }
}

public sealed class ActivateEndpointCredentialHandler(
    IEndpointRepository repository,
    IEndpointCredentialInvalidationPublisher invalidationPublisher,
    TimeProvider timeProvider,
    EndpointCredentialOptions options)
{
    public async Task<Endpoint> HandleAsync(ActivateEndpointCredentialCommand command, CancellationToken cancellationToken = default)
    {
        var endpoint = await SetEndpointCredentialHandler.GetApiKeyEndpointAsync(repository, command.EndpointId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (command.FallbackExpiresAt is { } expiry && expiry - now > options.MaxCredentialFallbackWindow)
        {
            throw new ValidationException($"Credential fallback cannot exceed {options.MaxCredentialFallbackWindow}.");
        }
        endpoint.ActivateCredential(command.ActiveSlot, command.FallbackExpiresAt, now);
        await repository.UpdateAsync(endpoint, cancellationToken);
        await invalidationPublisher.PublishAsync(CreateInvalidation(endpoint), cancellationToken);
        return endpoint;
    }

    internal static EndpointCredentialInvalidation CreateInvalidation(Endpoint endpoint) =>
        new(endpoint.Id, new[] { endpoint.PrimarySecretRef, endpoint.SecondarySecretRef }.OfType<string>().ToArray());
}

public sealed class DisableEndpointCredentialFallbackHandler(
    IEndpointRepository repository,
    IEndpointCredentialInvalidationPublisher invalidationPublisher)
{
    public async Task<Endpoint> HandleAsync(DisableEndpointCredentialFallbackCommand command, CancellationToken cancellationToken = default)
    {
        var endpoint = await SetEndpointCredentialHandler.GetApiKeyEndpointAsync(repository, command.EndpointId, cancellationToken);
        endpoint.DisableCredentialFallback();
        await repository.UpdateAsync(endpoint, cancellationToken);
        await invalidationPublisher.PublishAsync(ActivateEndpointCredentialHandler.CreateInvalidation(endpoint), cancellationToken);
        return endpoint;
    }
}

public sealed class RemoveEndpointCredentialHandler(
    IEndpointRepository repository,
    IEndpointCredentialInvalidationPublisher invalidationPublisher,
    IEndpointCredentialRetirementQueue retirementQueue,
    TimeProvider timeProvider)
{
    public async Task<Endpoint> HandleAsync(RemoveEndpointCredentialCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Slot == EndpointCredentialSlot.Primary)
        {
            throw new ConflictException("The Primary credential slot cannot be removed.");
        }
        var endpoint = await SetEndpointCredentialHandler.GetApiKeyEndpointAsync(repository, command.EndpointId, cancellationToken);
        var removedRef = endpoint.SecondarySecretRef;
        try
        {
            endpoint.RemoveSecondarySecret(timeProvider.GetUtcNow());
        }
        catch (DomainException exception)
        {
            throw new ConflictException(exception.Message);
        }
        if (removedRef is null)
        {
            throw new ConflictException("The Secondary credential slot is not configured.");
        }
        await repository.UpdateAsync(endpoint, cancellationToken);
        await invalidationPublisher.PublishAsync(
            new EndpointCredentialInvalidation(endpoint.Id, new[] { removedRef }), cancellationToken);
        retirementQueue.ScheduleRetirement(endpoint.Id, removedRef);
        return endpoint;
    }
}