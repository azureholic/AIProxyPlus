using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentValidation;

namespace AiProxy.Application.Models;

public sealed record CreateModelCommand(
    string Name,
    RoutingStrategy Strategy = RoutingStrategy.Priority);

public sealed class CreateModelValidator : AbstractValidator<CreateModelCommand>
{
    public CreateModelValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateModelHandler(
    IModelRepository repository,
    IValidator<CreateModelCommand> validator)
{
    public async Task<Model> HandleAsync(
        CreateModelCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        if (await repository.ExistsByNameAsync(command.Name, cancellationToken))
        {
            throw new ConflictException($"A model named '{command.Name}' already exists.");
        }

        var model = Model.Create(Guid.NewGuid(), command.Name, command.Strategy);
        await repository.AddAsync(model, cancellationToken);
        return model;
    }
}
