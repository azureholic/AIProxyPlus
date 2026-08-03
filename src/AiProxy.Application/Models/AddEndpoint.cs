using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentValidation;

namespace AiProxy.Application.Models;

public sealed record AddEndpointCommand(
    string ModelName,
    Guid EndpointId,
    int Priority,
    bool Enabled = true);

public sealed class AddEndpointValidator : AbstractValidator<AddEndpointCommand>
{
    public AddEndpointValidator()
    {
        RuleFor(c => c.ModelName).NotEmpty();
        RuleFor(c => c.EndpointId).NotEmpty();
        RuleFor(c => c.Priority).GreaterThanOrEqualTo(1);
    }
}

public sealed class AddEndpointHandler(
    IModelRepository repository,
    IEndpointRepository endpointRepository,
    IValidator<AddEndpointCommand> validator)
{
    public async Task<ModelEndpoint> HandleAsync(
        AddEndpointCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var model = await repository.GetByNameAsync(command.ModelName, cancellationToken)
            ?? throw new NotFoundException($"Model '{command.ModelName}' was not found.");

        var endpoint = await endpointRepository.GetByIdAsync(command.EndpointId, cancellationToken)
            ?? throw new NotFoundException($"Endpoint '{command.EndpointId}' was not found.");
        var assignment = ModelEndpoint.Create(model.Id, endpoint, command.Priority, command.Enabled);
        model.AddEndpoint(assignment);
        await repository.UpdateAsync(model, cancellationToken);
        return assignment;
    }
}
