using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentValidation;

namespace AiProxy.Application.ModelDeployments;

public sealed record UpdateEndpointCommand(
    string ModelDeploymentName,
    Guid EndpointId,
    string? BaseUrl = null,
    string? ManagedIdentityScope = null,
    int? Priority = null,
    bool? Enabled = null,
    int? TimeoutSeconds = null,
    int? MaxRetries = null,
    string? ManagedIdentityTenantId = null);

public sealed class UpdateEndpointValidator : AbstractValidator<UpdateEndpointCommand>
{
    public UpdateEndpointValidator()
    {
        RuleFor(c => c.ModelDeploymentName).NotEmpty();
        RuleFor(c => c.BaseUrl!)
            .Must(AddEndpointValidator.BeAllowedUrl)
            .WithMessage("Endpoint.BaseUrl must be an absolute https:// URL (http:// is only allowed for localhost).")
            .When(c => c.BaseUrl is not null);
        RuleFor(c => c.ManagedIdentityScope!).NotEmpty().When(c => c.ManagedIdentityScope is not null);
        RuleFor(c => c.ManagedIdentityTenantId!)
            .Must(tenantId => string.IsNullOrWhiteSpace(tenantId) || Guid.TryParse(tenantId, out _))
            .WithMessage("ManagedIdentityTenantId must be a valid tenant GUID.")
            .When(c => c.ManagedIdentityTenantId is not null);
        RuleFor(c => c.Priority!.Value).GreaterThanOrEqualTo(1).When(c => c.Priority.HasValue);
        RuleFor(c => c.TimeoutSeconds!.Value).GreaterThanOrEqualTo(1).When(c => c.TimeoutSeconds.HasValue);
        RuleFor(c => c.MaxRetries!.Value).GreaterThanOrEqualTo(0).When(c => c.MaxRetries.HasValue);
    }
}

public sealed class UpdateEndpointHandler(
    IModelDeploymentRepository repository,
    IValidator<UpdateEndpointCommand> validator)
{
    public async Task<Endpoint> HandleAsync(UpdateEndpointCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var deployment = await repository.GetByNameAsync(command.ModelDeploymentName, cancellationToken)
            ?? throw new NotFoundException($"Model deployment '{command.ModelDeploymentName}' was not found.");

        var endpoint = deployment.Endpoints.FirstOrDefault(e => e.Id == command.EndpointId)
            ?? throw new NotFoundException($"Endpoint '{command.EndpointId}' was not found on deployment '{command.ModelDeploymentName}'.");

        if (command.BaseUrl is { } baseUrl)
        {
            endpoint.UpdateBaseUrl(baseUrl);
        }

        if (command.ManagedIdentityScope is { } managedIdentityScope)
        {
            endpoint.UpdateManagedIdentityScope(managedIdentityScope);
        }

        if (command.ManagedIdentityTenantId is not null)
        {
            endpoint.UpdateManagedIdentityTenantId(command.ManagedIdentityTenantId);
        }

        if (command.Priority is { } priority)
        {
            deployment.ChangeEndpointPriority(endpoint.Id, priority);
        }

        if (command.Enabled is { } enabled)
        {
            if (enabled)
            {
                endpoint.Enable();
            }
            else
            {
                endpoint.Disable();
            }
        }

        if (command.TimeoutSeconds is { } timeoutSeconds)
        {
            endpoint.UpdateTimeoutSeconds(timeoutSeconds);
        }

        if (command.MaxRetries is { } maxRetries)
        {
            endpoint.UpdateMaxRetries(maxRetries);
        }

        await repository.UpdateAsync(deployment, cancellationToken);
        return endpoint;
    }
}
