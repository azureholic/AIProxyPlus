using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using FluentValidation;

namespace AIProxyPlus.Application.Models;

public sealed record DeleteModelCommand(string Name);

public sealed class DeleteModelValidator : AbstractValidator<DeleteModelCommand>
{
    public DeleteModelValidator()
    {
        RuleFor(c => c.Name).NotEmpty();
    }
}

public sealed class DeleteModelHandler(
    IModelRepository repository,
    IValidator<DeleteModelCommand> validator)
{
    public async Task HandleAsync(DeleteModelCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var model = await repository.GetByNameAsync(command.Name, cancellationToken)
            ?? throw new NotFoundException($"Model '{command.Name}' was not found.");

        await repository.DeleteAsync(model.Id, cancellationToken);
    }
}
