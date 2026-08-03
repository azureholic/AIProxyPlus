using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentValidation;

namespace AiProxy.Application.ModelDeployments;

public sealed record UpdateEndpointCommand(
    string ModelDeploymentName,
    Guid EndpointId,
    int? Priority = null,
    bool? Enabled = null);

public sealed class UpdateEndpointValidator : AbstractValidator<UpdateEndpointCommand>
{
    public UpdateEndpointValidator()
    {
        RuleFor(c => c.ModelDeploymentName).NotEmpty();
        RuleFor(c => c.Priority!.Value).GreaterThanOrEqualTo(1).When(c => c.Priority.HasValue);
    }
}

public sealed class UpdateEndpointHandler(
    IModelDeploymentRepository repository,
    IValidator<UpdateEndpointCommand> validator)
{
    public async Task<ModelDeploymentEndpoint> HandleAsync(
        UpdateEndpointCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var deployment = await repository.GetByNameAsync(command.ModelDeploymentName, cancellationToken)
            ?? throw new NotFoundException($"Model deployment '{command.ModelDeploymentName}' was not found.");

        var endpoint = deployment.Endpoints.FirstOrDefault(e => e.Id == command.EndpointId)
            ?? throw new NotFoundException($"Endpoint '{command.EndpointId}' was not found on deployment '{command.ModelDeploymentName}'.");

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

        await repository.UpdateAsync(deployment, cancellationToken);
        return endpoint;
    }
}
