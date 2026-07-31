using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using FluentValidation;

namespace AiProxy.Application.ModelDeployments;

public sealed record DeleteModelDeploymentCommand(string Name);

public sealed class DeleteModelDeploymentValidator : AbstractValidator<DeleteModelDeploymentCommand>
{
    public DeleteModelDeploymentValidator()
    {
        RuleFor(c => c.Name).NotEmpty();
    }
}

public sealed class DeleteModelDeploymentHandler(
    IModelDeploymentRepository repository,
    IValidator<DeleteModelDeploymentCommand> validator)
{
    public async Task HandleAsync(DeleteModelDeploymentCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var deployment = await repository.GetByNameAsync(command.Name, cancellationToken)
            ?? throw new NotFoundException($"Model deployment '{command.Name}' was not found.");

        await repository.DeleteAsync(deployment.Id, cancellationToken);
    }
}
