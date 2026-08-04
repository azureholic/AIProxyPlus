using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentValidation;

namespace AiProxy.Application.Models;

public sealed record UpdateModelCommand(
    string Name,
    RoutingStrategy Strategy,
    IReadOnlyCollection<ModelCapability>? Capabilities = null);

public sealed class UpdateModelValidator : AbstractValidator<UpdateModelCommand>
{
    public UpdateModelValidator()
    {
        RuleFor(c => c.Name).NotEmpty();
        RuleFor(c => c.Capabilities)
            .NotEmpty()
            .When(c => c.Capabilities is not null)
            .Must(capabilities => capabilities is null || capabilities.Distinct().Count() == capabilities.Count)
            .WithMessage("Model capabilities must not contain duplicates.");
    }
}

public sealed class UpdateModelHandler(
    IModelRepository repository,
    IValidator<UpdateModelCommand> validator)
{
    public async Task<Model> HandleAsync(
        UpdateModelCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var model = await repository.GetByNameAsync(command.Name, cancellationToken)
            ?? throw new NotFoundException($"Model '{command.Name}' was not found.");

        model.SetStrategy(command.Strategy);
        if (command.Capabilities is not null)
        {
            model.ReplaceCapabilities(command.Capabilities);
        }

        await repository.UpdateAsync(model, cancellationToken);
        return model;
    }
}
