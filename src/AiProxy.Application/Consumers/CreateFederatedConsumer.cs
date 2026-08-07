using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentValidation;

namespace AiProxy.Application.Consumers;

public sealed record CreateOidcConsumerCommand(string Name, string Issuer, string Subject);

public sealed record CreateManagedIdentityConsumerCommand(string Name, Guid TenantId, Guid ObjectId);

public sealed class CreateOidcConsumerValidator : AbstractValidator<CreateOidcConsumerCommand>
{
    public CreateOidcConsumerValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Issuer).NotEmpty().MaximumLength(500);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(500);
    }
}

public sealed class CreateManagedIdentityConsumerValidator : AbstractValidator<CreateManagedIdentityConsumerCommand>
{
    public CreateManagedIdentityConsumerValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.ObjectId).NotEmpty();
    }
}

public sealed class CreateOidcConsumerHandler(
    IConsumerRepository repository,
    IValidator<CreateOidcConsumerCommand> validator)
{
    public async Task<Consumer> HandleAsync(
        CreateOidcConsumerCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var issuer = NormalizeIssuer(command.Issuer);
        if (await repository.ExistsByNameAsync(command.Name, cancellationToken) ||
            await repository.GetByOidcIdentityAsync(issuer, command.Subject, cancellationToken) is not null)
        {
            throw new ConflictException("A consumer with this name or OIDC identity already exists.");
        }

        var consumer = Consumer.CreateOidc(Guid.NewGuid(), command.Name, issuer, command.Subject);
        await repository.AddAsync(consumer, cancellationToken);
        return consumer;
    }

    private static string NormalizeIssuer(string issuer) =>
        Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
            ? uri.AbsoluteUri.TrimEnd('/')
            : issuer;
}

public sealed class CreateManagedIdentityConsumerHandler(
    IConsumerRepository repository,
    IValidator<CreateManagedIdentityConsumerCommand> validator)
{
    public async Task<Consumer> HandleAsync(
        CreateManagedIdentityConsumerCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        if (await repository.ExistsByNameAsync(command.Name, cancellationToken) ||
            await repository.GetByManagedIdentityAsync(command.TenantId, command.ObjectId, cancellationToken) is not null)
        {
            throw new ConflictException("A consumer with this name or managed identity already exists.");
        }

        var consumer = Consumer.CreateManagedIdentity(
            Guid.NewGuid(), command.Name, command.TenantId, command.ObjectId);
        await repository.AddAsync(consumer, cancellationToken);
        return consumer;
    }
}