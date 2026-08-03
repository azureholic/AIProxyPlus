using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Enums;
using FluentValidation;

namespace AiProxy.Application.Endpoints;

public sealed record DeleteEndpointCommand(Guid EndpointId);

public sealed class DeleteEndpointValidator : AbstractValidator<DeleteEndpointCommand>
{
    public DeleteEndpointValidator()
    {
        RuleFor(command => command.EndpointId).NotEmpty();
    }
}

public sealed class DeleteEndpointHandler(
    IEndpointRepository repository,
    ISecretResolver secretResolver,
    IValidator<DeleteEndpointCommand> validator)
{
    public async Task HandleAsync(DeleteEndpointCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var endpoint = await repository.GetByIdAsync(command.EndpointId, cancellationToken)
            ?? throw new NotFoundException($"Endpoint '{command.EndpointId}' was not found.");

        if (await repository.IsAssignedAsync(endpoint.Id, cancellationToken))
        {
            throw new ConflictException($"Endpoint '{endpoint.Name}' cannot be deleted while it is assigned to a model.");
        }

        await repository.DeleteAsync(endpoint.Id, cancellationToken);

        if (endpoint.AuthMode == AuthMode.ApiKey && endpoint.SecretRef is not null)
        {
            await secretResolver.DeleteAsync(endpoint.SecretRef, cancellationToken);
        }
    }
}