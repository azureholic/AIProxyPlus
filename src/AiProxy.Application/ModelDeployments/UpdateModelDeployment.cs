using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentValidation;

namespace AiProxy.Application.ModelDeployments;

public sealed record UpdateModelDeploymentCommand(string Name, RoutingStrategy Strategy);

public sealed class UpdateModelDeploymentValidator : AbstractValidator<UpdateModelDeploymentCommand>
{
    public UpdateModelDeploymentValidator()
    {
        RuleFor(c => c.Name).NotEmpty();
    }
}

public sealed class UpdateModelDeploymentHandler(
    IModelDeploymentRepository repository,
    IValidator<UpdateModelDeploymentCommand> validator)
{
    public async Task<ModelDeployment> HandleAsync(
        UpdateModelDeploymentCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var deployment = await repository.GetByNameAsync(command.Name, cancellationToken)
            ?? throw new NotFoundException($"Model deployment '{command.Name}' was not found.");

        deployment.SetStrategy(command.Strategy);

        await repository.UpdateAsync(deployment, cancellationToken);
        return deployment;
    }
}
