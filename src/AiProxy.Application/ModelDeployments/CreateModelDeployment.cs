using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentValidation;

namespace AiProxy.Application.ModelDeployments;

public sealed record CreateModelDeploymentCommand(
    string Name,
    bool StickySessionEnabled = false,
    RoutingStrategy Strategy = RoutingStrategy.Priority);

public sealed class CreateModelDeploymentValidator : AbstractValidator<CreateModelDeploymentCommand>
{
    public CreateModelDeploymentValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateModelDeploymentHandler(
    IModelDeploymentRepository repository,
    IValidator<CreateModelDeploymentCommand> validator)
{
    public async Task<ModelDeployment> HandleAsync(
        CreateModelDeploymentCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        if (await repository.ExistsByNameAsync(command.Name, cancellationToken))
        {
            throw new ConflictException($"A model deployment named '{command.Name}' already exists.");
        }

        var deployment = ModelDeployment.Create(Guid.NewGuid(), command.Name, command.StickySessionEnabled, command.Strategy);
        await repository.AddAsync(deployment, cancellationToken);
        return deployment;
    }
}
