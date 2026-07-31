using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using FluentValidation;

namespace AiProxy.Application.ModelDeployments;

public sealed record RemoveEndpointCommand(string ModelDeploymentName, Guid EndpointId);

public sealed class RemoveEndpointValidator : AbstractValidator<RemoveEndpointCommand>
{
    public RemoveEndpointValidator()
    {
        RuleFor(c => c.ModelDeploymentName).NotEmpty();
    }
}

public sealed class RemoveEndpointHandler(
    IModelDeploymentRepository repository,
    IValidator<RemoveEndpointCommand> validator)
{
    public async Task HandleAsync(RemoveEndpointCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var deployment = await repository.GetByNameAsync(command.ModelDeploymentName, cancellationToken)
            ?? throw new NotFoundException($"Model deployment '{command.ModelDeploymentName}' was not found.");

        if (deployment.Endpoints.All(e => e.Id != command.EndpointId))
        {
            throw new NotFoundException($"Endpoint '{command.EndpointId}' was not found on deployment '{command.ModelDeploymentName}'.");
        }

        deployment.RemoveEndpoint(command.EndpointId);
        await repository.UpdateAsync(deployment, cancellationToken);
    }
}
