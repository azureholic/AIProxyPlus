using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using FluentValidation;

namespace AIProxyPlus.Application.Models;

public sealed record RemoveEndpointCommand(string ModelName, Guid EndpointId);

public sealed class RemoveEndpointValidator : AbstractValidator<RemoveEndpointCommand>
{
    public RemoveEndpointValidator()
    {
        RuleFor(c => c.ModelName).NotEmpty();
    }
}

public sealed class RemoveEndpointHandler(
    IModelRepository repository,
    IValidator<RemoveEndpointCommand> validator)
{
    public async Task HandleAsync(RemoveEndpointCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var model = await repository.GetByNameAsync(command.ModelName, cancellationToken)
            ?? throw new NotFoundException($"Model '{command.ModelName}' was not found.");

        if (model.Endpoints.All(e => e.Id != command.EndpointId))
        {
            throw new NotFoundException($"Endpoint '{command.EndpointId}' was not found on model '{command.ModelName}'.");
        }

        model.RemoveEndpoint(command.EndpointId);
        await repository.UpdateAsync(model, cancellationToken);
    }
}
