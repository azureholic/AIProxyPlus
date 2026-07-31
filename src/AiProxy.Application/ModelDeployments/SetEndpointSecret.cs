using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Enums;
using FluentValidation;

namespace AiProxy.Application.ModelDeployments;

public sealed record SetEndpointSecretCommand(string ModelDeploymentName, Guid EndpointId, string ApiKeyPlaintext);

public sealed class SetEndpointSecretValidator : AbstractValidator<SetEndpointSecretCommand>
{
    public SetEndpointSecretValidator()
    {
        RuleFor(c => c.ModelDeploymentName).NotEmpty();
        RuleFor(c => c.ApiKeyPlaintext).NotEmpty();
    }
}

/// <summary>
/// Rotates the plaintext value stored at an endpoint's existing Vault path. No repository write is needed —
/// `Endpoint.SecretRef` (the path) never changes, only the value Vault holds at that path (architecture.md §8).
/// </summary>
public sealed class SetEndpointSecretHandler(
    IModelDeploymentRepository repository,
    ISecretResolver secretResolver,
    IValidator<SetEndpointSecretCommand> validator)
{
    public async Task HandleAsync(SetEndpointSecretCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var deployment = await repository.GetByNameAsync(command.ModelDeploymentName, cancellationToken)
            ?? throw new NotFoundException($"Model deployment '{command.ModelDeploymentName}' was not found.");

        var endpoint = deployment.Endpoints.FirstOrDefault(e => e.Id == command.EndpointId)
            ?? throw new NotFoundException($"Endpoint '{command.EndpointId}' was not found on deployment '{command.ModelDeploymentName}'.");

        if (endpoint.AuthMode != AuthMode.ApiKey || endpoint.SecretRef is null)
        {
            throw new ConflictException("Cannot set an API key secret on a non-ApiKey-mode endpoint.");
        }

        await secretResolver.StoreAsync(endpoint.SecretRef, command.ApiKeyPlaintext, cancellationToken);
    }
}
