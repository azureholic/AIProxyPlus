using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Domain.Entities;
using FluentValidation;

namespace AIProxyPlus.Application.Endpoints;

public sealed record UpdateEndpointConfigurationCommand(
    Guid EndpointId,
    string Name,
    string BaseUrl,
    string? ManagedIdentityScope,
    int TimeoutSeconds,
    int MaxRetries,
    string? ManagedIdentityTenantId = null);

public sealed class UpdateEndpointConfigurationValidator : AbstractValidator<UpdateEndpointConfigurationCommand>
{
    public UpdateEndpointConfigurationValidator()
    {
        RuleFor(command => command.EndpointId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.BaseUrl)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(CreateEndpointValidator.BeAllowedUrl)
            .WithMessage("Endpoint.BaseUrl must be an absolute https:// URL (http:// is only allowed for localhost).");
        RuleFor(command => command.ManagedIdentityTenantId)
            .Must(tenantId => string.IsNullOrWhiteSpace(tenantId) || Guid.TryParse(tenantId, out _))
            .WithMessage("ManagedIdentityTenantId must be a valid tenant GUID.");
        RuleFor(command => command.TimeoutSeconds).InclusiveBetween(1, Endpoint.MaxTimeoutSeconds);
        RuleFor(command => command.MaxRetries).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateEndpointConfigurationHandler(
    IEndpointRepository repository,
    IValidator<UpdateEndpointConfigurationCommand> validator)
{
    public async Task<Endpoint> HandleAsync(
        UpdateEndpointConfigurationCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var endpoint = await repository.GetByIdAsync(command.EndpointId, cancellationToken)
            ?? throw new NotFoundException($"Endpoint '{command.EndpointId}' was not found.");

        if (await repository.ExistsByNameAsync(command.Name, endpoint.Id, cancellationToken))
        {
            throw new ConflictException($"An endpoint named '{command.Name}' already exists.");
        }

        endpoint.UpdateName(command.Name);
        endpoint.UpdateBaseUrl(command.BaseUrl);
        endpoint.UpdateTimeoutSeconds(command.TimeoutSeconds);
        endpoint.UpdateMaxRetries(command.MaxRetries);

        if (endpoint.AuthMode == Domain.Enums.AuthMode.ManagedIdentity)
        {
            endpoint.UpdateManagedIdentityScope(command.ManagedIdentityScope!);
            endpoint.UpdateManagedIdentityTenantId(command.ManagedIdentityTenantId);
        }

        await repository.UpdateAsync(endpoint, cancellationToken);
        return endpoint;
    }
}