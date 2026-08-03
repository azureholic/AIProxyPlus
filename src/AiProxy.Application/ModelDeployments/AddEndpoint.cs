using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentValidation;

namespace AiProxy.Application.ModelDeployments;

public sealed record AddEndpointCommand(
    string ModelDeploymentName,
    Guid EndpointId,
    int Priority,
    bool Enabled = true);

public sealed class AddEndpointValidator : AbstractValidator<AddEndpointCommand>
{
    public AddEndpointValidator()
    {
        RuleFor(c => c.ModelDeploymentName).NotEmpty();
        RuleFor(c => c.EndpointId).NotEmpty();
        RuleFor(c => c.Priority).GreaterThanOrEqualTo(1);
    }
}

public sealed class AddEndpointHandler(
    IModelDeploymentRepository repository,
    IEndpointRepository endpointRepository,
    IValidator<AddEndpointCommand> validator)
{
    public async Task<ModelDeploymentEndpoint> HandleAsync(
        AddEndpointCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var deployment = await repository.GetByNameAsync(command.ModelDeploymentName, cancellationToken)
            ?? throw new NotFoundException($"Model deployment '{command.ModelDeploymentName}' was not found.");

        var endpoint = await endpointRepository.GetByIdAsync(command.EndpointId, cancellationToken)
            ?? throw new NotFoundException($"Endpoint '{command.EndpointId}' was not found.");
        var assignment = ModelDeploymentEndpoint.Create(deployment.Id, endpoint, command.Priority, command.Enabled);
        deployment.AddEndpoint(assignment);
        await repository.UpdateAsync(deployment, cancellationToken);
        return assignment;
    }
}
