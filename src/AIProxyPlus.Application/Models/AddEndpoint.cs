using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Domain.Entities;
using FluentValidation;

namespace AIProxyPlus.Application.Models;

public sealed record AddEndpointCommand(
    string ModelName,
    Guid EndpointId,
    Guid ApiRouteId,
    int Priority,
    bool Enabled = true);

public sealed class AddEndpointValidator : AbstractValidator<AddEndpointCommand>
{
    public AddEndpointValidator()
    {
        RuleFor(c => c.ModelName).NotEmpty();
        RuleFor(c => c.EndpointId).NotEmpty();
        RuleFor(c => c.ApiRouteId).NotEmpty();
        RuleFor(c => c.Priority).GreaterThanOrEqualTo(1);
    }
}

public sealed class AddEndpointHandler(
    IModelRepository repository,
    IEndpointRepository endpointRepository,
    IApiRouteRepository apiRouteRepository,
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
        var apiRoute = await apiRouteRepository.GetByIdAsync(command.ApiRouteId, cancellationToken)
            ?? throw new NotFoundException($"API route '{command.ApiRouteId}' was not found.");
        var assignment = ModelEndpoint.Create(Guid.NewGuid(), model.Id, endpoint, apiRoute, command.Priority, command.Enabled);
        model.AddEndpoint(assignment);
        await repository.UpdateAsync(model, cancellationToken);
        return assignment;
    }
}
