using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using FluentValidation;

namespace AIProxyPlus.Application.Models;

public sealed record CreateModelCommand(
    string Name,
    RoutingStrategy Strategy = RoutingStrategy.Priority,
    IReadOnlyCollection<ModelCapability>? Capabilities = null);

public sealed class CreateModelValidator : AbstractValidator<CreateModelCommand>
{
    public CreateModelValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Capabilities)
            .NotEmpty()
            .When(c => c.Capabilities is not null)
            .Must(capabilities => capabilities is null || capabilities.Distinct().Count() == capabilities.Count)
            .WithMessage("Model capabilities must not contain duplicates.");
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

        var model = Model.Create(
            Guid.NewGuid(),
            command.Name,
            command.Strategy,
            capabilities: command.Capabilities);
        await repository.AddAsync(model, cancellationToken);
        return model;
    }
}
