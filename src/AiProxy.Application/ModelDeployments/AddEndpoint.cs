using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentValidation;

namespace AiProxy.Application.ModelDeployments;

public sealed record AddEndpointCommand(
    string ModelDeploymentName,
    string BaseUrl,
    ApiFamily ApiFamily,
    int Priority,
    AuthMode AuthMode,
    string? ApiKeyPlaintext = null,
    string? ManagedIdentityScope = null,
    bool Enabled = true,
    int TimeoutSeconds = 100,
    int MaxRetries = 2,
    string? ManagedIdentityTenantId = null);

public sealed class AddEndpointValidator : AbstractValidator<AddEndpointCommand>
{
    public AddEndpointValidator()
    {
        RuleFor(c => c.ModelDeploymentName).NotEmpty();
        RuleFor(c => c.Priority).GreaterThanOrEqualTo(1);
        RuleFor(c => c.TimeoutSeconds).GreaterThanOrEqualTo(1);
        RuleFor(c => c.MaxRetries).GreaterThanOrEqualTo(0);

        RuleFor(c => c.BaseUrl)
            .NotEmpty()
            .Must(BeAllowedUrl)
            .WithMessage("Endpoint.BaseUrl must be an absolute https:// URL (http:// is only allowed for localhost).");

        RuleFor(c => c.ApiKeyPlaintext)
            .NotEmpty()
            .WithMessage("An ApiKey-mode endpoint requires ApiKeyPlaintext.")
            .When(c => c.AuthMode == AuthMode.ApiKey);

        RuleFor(c => c.ManagedIdentityScope)
            .NotEmpty()
            .WithMessage("A ManagedIdentity-mode endpoint requires ManagedIdentityScope.")
            .When(c => c.AuthMode == AuthMode.ManagedIdentity);

        RuleFor(c => c.ManagedIdentityTenantId)
            .Must(tenantId => string.IsNullOrWhiteSpace(tenantId) || Guid.TryParse(tenantId, out _))
            .WithMessage("ManagedIdentityTenantId must be a valid tenant GUID.");

        RuleFor(c => c.ManagedIdentityTenantId)
            .Empty()
            .WithMessage("ManagedIdentityTenantId is only valid for ManagedIdentity auth mode.")
            .When(c => c.AuthMode != AuthMode.ManagedIdentity);
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

public sealed class AddEndpointHandler(
    IModelDeploymentRepository repository,
    ISecretResolver secretResolver,
    IValidator<AddEndpointCommand> validator)
{
    public async Task<Endpoint> HandleAsync(AddEndpointCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var deployment = await repository.GetByNameAsync(command.ModelDeploymentName, cancellationToken)
            ?? throw new NotFoundException($"Model deployment '{command.ModelDeploymentName}' was not found.");

        var endpointId = Guid.NewGuid();

        string? secretRef = null;
        if (command.AuthMode == AuthMode.ApiKey)
        {
            secretRef = $"endpoints/{endpointId}";
            await secretResolver.StoreAsync(secretRef, command.ApiKeyPlaintext!, cancellationToken);
        }

        var endpoint = Endpoint.Create(
            endpointId,
            deployment.Id,
            command.BaseUrl,
            command.ApiFamily,
            command.Priority,
            command.AuthMode,
            secretRef,
            command.ManagedIdentityScope,
            command.Enabled,
            command.TimeoutSeconds,
            command.MaxRetries,
            command.ManagedIdentityTenantId);

        deployment.AddEndpoint(endpoint);
        await repository.UpdateAsync(deployment, cancellationToken);
        return endpoint;
    }
}
