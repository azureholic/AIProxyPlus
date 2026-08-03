using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentValidation;

namespace AiProxy.Application.Models;

public sealed record UpdateEndpointCommand(
    string ModelName,
    Guid EndpointId,
    int? Priority = null,
    bool? Enabled = null);

public sealed class UpdateEndpointValidator : AbstractValidator<UpdateEndpointCommand>
{
    public UpdateEndpointValidator()
    {
        RuleFor(c => c.ModelName).NotEmpty();
        RuleFor(c => c.Priority!.Value).GreaterThanOrEqualTo(1).When(c => c.Priority.HasValue);
    }
}

public sealed class UpdateEndpointHandler(
    IModelRepository repository,
    IValidator<UpdateEndpointCommand> validator)
{
    public async Task<ModelEndpoint> HandleAsync(
        UpdateEndpointCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var model = await repository.GetByNameAsync(command.ModelName, cancellationToken)
            ?? throw new NotFoundException($"Model '{command.ModelName}' was not found.");

        var endpoint = model.Endpoints.FirstOrDefault(e => e.Id == command.EndpointId)
            ?? throw new NotFoundException($"Endpoint '{command.EndpointId}' was not found on model '{command.ModelName}'.");

        if (command.Priority is { } priority)
        {
            model.ChangeEndpointPriority(endpoint.Id, priority);
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

        await repository.UpdateAsync(model, cancellationToken);
        return endpoint;
    }
}
