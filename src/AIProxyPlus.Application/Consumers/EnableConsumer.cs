using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Domain.Entities;
using FluentValidation;

namespace AIProxyPlus.Application.Consumers;

public sealed record EnableConsumerCommand(string Name);

public sealed class EnableConsumerValidator : AbstractValidator<EnableConsumerCommand>
{
    public EnableConsumerValidator()
    {
        RuleFor(command => command.Name).NotEmpty();
    }
}

public sealed class EnableConsumerHandler(
    IConsumerRepository repository,
    IValidator<EnableConsumerCommand> validator)
{
    public async Task<Consumer> HandleAsync(
        EnableConsumerCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var consumer = await repository.GetByNameAsync(command.Name, cancellationToken)
            ?? throw new NotFoundException($"Consumer '{command.Name}' was not found.");

        if (consumer.RevokedAt is not null)
        {
            throw new ConflictException("A revoked consumer cannot be enabled.");
        }

        consumer.Enable();
        await repository.UpdateAsync(consumer, cancellationToken);
        return consumer;
    }
}