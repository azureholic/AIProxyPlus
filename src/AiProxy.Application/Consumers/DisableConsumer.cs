using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentValidation;

namespace AiProxy.Application.Consumers;

public sealed record DisableConsumerCommand(string Name);

public sealed class DisableConsumerValidator : AbstractValidator<DisableConsumerCommand>
{
    public DisableConsumerValidator()
    {
        RuleFor(command => command.Name).NotEmpty();
    }
}

public sealed class DisableConsumerHandler(
    IConsumerRepository repository,
    IValidator<DisableConsumerCommand> validator)
{
    public async Task<Consumer> HandleAsync(
        DisableConsumerCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var consumer = await repository.GetByNameAsync(command.Name, cancellationToken)
            ?? throw new NotFoundException($"Consumer '{command.Name}' was not found.");

        if (consumer.RevokedAt is not null)
        {
            throw new ConflictException("A revoked consumer cannot be disabled.");
        }

        consumer.Disable();
        await repository.UpdateAsync(consumer, cancellationToken);
        return consumer;
    }
}