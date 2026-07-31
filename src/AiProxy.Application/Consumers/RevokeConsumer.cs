using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentValidation;

namespace AiProxy.Application.Consumers;

public sealed record RevokeConsumerCommand(string Name);

public sealed class RevokeConsumerValidator : AbstractValidator<RevokeConsumerCommand>
{
    public RevokeConsumerValidator()
    {
        RuleFor(c => c.Name).NotEmpty();
    }
}

public sealed class RevokeConsumerHandler(
    IConsumerRepository repository,
    IValidator<RevokeConsumerCommand> validator)
{
    public async Task<Consumer> HandleAsync(RevokeConsumerCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var consumer = await repository.GetByNameAsync(command.Name, cancellationToken)
            ?? throw new NotFoundException($"Consumer '{command.Name}' was not found.");

        consumer.Revoke();
        await repository.UpdateAsync(consumer, cancellationToken);
        return consumer;
    }
}
