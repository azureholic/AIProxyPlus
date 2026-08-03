using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Enums;
using FluentValidation;

namespace AiProxy.Application.Endpoints;

public sealed record SetEndpointSecretCommand(Guid EndpointId, string ApiKeyPlaintext);

public sealed class SetEndpointSecretValidator : AbstractValidator<SetEndpointSecretCommand>
{
    public SetEndpointSecretValidator()
    {
        RuleFor(command => command.EndpointId).NotEmpty();
        RuleFor(command => command.ApiKeyPlaintext).NotEmpty();
    }
}

public sealed class SetEndpointSecretHandler(
    IEndpointRepository repository,
    ISecretResolver secretResolver,
    IValidator<SetEndpointSecretCommand> validator)
{
    public async Task HandleAsync(SetEndpointSecretCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var endpoint = await repository.GetByIdAsync(command.EndpointId, cancellationToken)
            ?? throw new NotFoundException($"Endpoint '{command.EndpointId}' was not found.");

        if (endpoint.AuthMode != AuthMode.ApiKey || endpoint.SecretRef is null)
        {
            throw new ConflictException("Cannot set an API key secret on a non-ApiKey-mode endpoint.");
        }

        await secretResolver.StoreAsync(endpoint.SecretRef, command.ApiKeyPlaintext, cancellationToken);
    }
}