using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using FluentValidation;

namespace AIProxyPlus.Application.ApiRoutes;

public sealed record CreateApiRouteCommand(string Name, Provider Provider, string PathPrefix);

public sealed record UpdateApiRouteCommand(Guid Id, string Name, Provider Provider, string PathPrefix);

public sealed record DeleteApiRouteCommand(Guid Id);

public sealed class CreateApiRouteValidator : AbstractValidator<CreateApiRouteCommand>
{
    public CreateApiRouteValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Provider).IsInEnum();
        RuleFor(command => command.PathPrefix).NotEmpty().MaximumLength(500);
    }
}

public sealed class UpdateApiRouteValidator : AbstractValidator<UpdateApiRouteCommand>
{
    public UpdateApiRouteValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Provider).IsInEnum();
        RuleFor(command => command.PathPrefix).NotEmpty().MaximumLength(500);
    }
}

public sealed class DeleteApiRouteValidator : AbstractValidator<DeleteApiRouteCommand>
{
    public DeleteApiRouteValidator() => RuleFor(command => command.Id).NotEmpty();
}

public sealed class CreateApiRouteHandler(
    IApiRouteRepository repository,
    IValidator<CreateApiRouteCommand> validator)
{
    public async Task<ApiRoute> HandleAsync(CreateApiRouteCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var route = ApiRoute.Create(Guid.NewGuid(), command.Name, command.Provider, command.PathPrefix);
        await EnsureUniqueAsync(repository, route, null, cancellationToken);
        await repository.AddAsync(route, cancellationToken);
        return route;
    }

    internal static async Task EnsureUniqueAsync(
        IApiRouteRepository repository,
        ApiRoute route,
        Guid? excludingId,
        CancellationToken cancellationToken)
    {
        if (await repository.ExistsByNameAsync(route.Name, excludingId, cancellationToken))
        {
            throw new ConflictException($"An API route named '{route.Name}' already exists.");
        }

        if (await repository.ExistsByProviderAndPathPrefixAsync(route.Provider, route.PathPrefix, excludingId, cancellationToken))
        {
            throw new ConflictException($"An API route for '{route.Provider}' and '{route.PathPrefix}' already exists.");
        }
    }
}

public sealed class UpdateApiRouteHandler(
    IApiRouteRepository repository,
    IValidator<UpdateApiRouteCommand> validator)
{
    public async Task<ApiRoute> HandleAsync(UpdateApiRouteCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var route = await repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException($"API route '{command.Id}' was not found.");
        route.Update(command.Name, command.Provider, command.PathPrefix);
        await CreateApiRouteHandler.EnsureUniqueAsync(repository, route, route.Id, cancellationToken);
        await repository.UpdateAsync(route, cancellationToken);
        return route;
    }
}

public sealed class DeleteApiRouteHandler(
    IApiRouteRepository repository,
    IValidator<DeleteApiRouteCommand> validator)
{
    public async Task HandleAsync(DeleteApiRouteCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var route = await repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException($"API route '{command.Id}' was not found.");
        if (await repository.IsAssignedAsync(route.Id, cancellationToken))
        {
            throw new ConflictException($"API route '{route.Name}' cannot be deleted while it is assigned to a model.");
        }

        await repository.DeleteAsync(route.Id, cancellationToken);
    }
}

public sealed class GetApiRoutesHandler(IApiRouteRepository repository)
{
    public Task<IReadOnlyList<ApiRoute>> HandleAsync(CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(cancellationToken);
}

public sealed class GetApiRouteHandler(IApiRouteRepository repository)
{
    public async Task<ApiRoute> HandleAsync(Guid id, CancellationToken cancellationToken = default) =>
        await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"API route '{id}' was not found.");
}