using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentValidation;

namespace AiProxy.Application.Endpoints;

public sealed record CreateEndpointCommand(
    string Name,
    string BaseUrl,
    AuthMode AuthMode,
    string? ApiKeyPlaintext = null,
    string? ManagedIdentityScope = null,
    int TimeoutSeconds = 100,
    int MaxRetries = 2,
    string? ManagedIdentityTenantId = null);

public sealed class CreateEndpointValidator : AbstractValidator<CreateEndpointCommand>
{
    public CreateEndpointValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.BaseUrl)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(BeAllowedUrl)
            .WithMessage("Endpoint.BaseUrl must be an absolute https:// URL (http:// is only allowed for localhost).");
        RuleFor(command => command.ApiKeyPlaintext)
            .NotEmpty()
            .When(command => command.AuthMode == AuthMode.ApiKey);
        RuleFor(command => command.ManagedIdentityScope)
            .NotEmpty()
            .When(command => command.AuthMode == AuthMode.ManagedIdentity);
        RuleFor(command => command.ManagedIdentityTenantId)
            .Must(tenantId => string.IsNullOrWhiteSpace(tenantId) || Guid.TryParse(tenantId, out _))
            .WithMessage("ManagedIdentityTenantId must be a valid tenant GUID.");
        RuleFor(command => command.ManagedIdentityTenantId)
            .Empty()
            .When(command => command.AuthMode != AuthMode.ManagedIdentity)
            .WithMessage("ManagedIdentityTenantId is only valid for ManagedIdentity auth mode.");
        RuleFor(command => command.TimeoutSeconds).GreaterThanOrEqualTo(1);
        RuleFor(command => command.MaxRetries).GreaterThanOrEqualTo(0);
    }

    internal static bool BeAllowedUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}

public sealed class CreateEndpointHandler(
    IEndpointRepository repository,
    ISecretResolver secretResolver,
    IValidator<CreateEndpointCommand> validator)
{
    public async Task<Endpoint> HandleAsync(
        CreateEndpointCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        if (await repository.ExistsByNameAsync(command.Name, cancellationToken: cancellationToken))
        {
            throw new ConflictException($"An endpoint named '{command.Name}' already exists.");
        }

        var endpointId = Guid.NewGuid();
        var secretRef = command.AuthMode == AuthMode.ApiKey
            ? $"endpoints/{endpointId}/primary/{Guid.NewGuid():n}"
            : null;
        var endpoint = Endpoint.Create(
            endpointId,
            command.Name,
            command.BaseUrl,
            command.AuthMode,
            secretRef,
            command.ManagedIdentityScope,
            command.TimeoutSeconds,
            command.MaxRetries,
            command.ManagedIdentityTenantId);

        if (secretRef is not null)
        {
            await secretResolver.StoreAsync(secretRef, command.ApiKeyPlaintext!, cancellationToken);
        }

        try
        {
            await repository.AddAsync(endpoint, cancellationToken);
        }
        catch
        {
            if (secretRef is not null)
            {
                await secretResolver.DeleteAsync(secretRef, cancellationToken);
            }

            throw;
        }

        return endpoint;
    }
}