using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using FluentValidation;

namespace AiProxy.Application.Consumers;

public sealed record DeleteConsumerPermanentlyCommand(string Name);

public sealed class DeleteConsumerPermanentlyValidator : AbstractValidator<DeleteConsumerPermanentlyCommand>
{
    public DeleteConsumerPermanentlyValidator()
    {
        RuleFor(command => command.Name).NotEmpty();
    }
}

public sealed class DeleteConsumerPermanentlyHandler(
    IConsumerRepository repository,
    IValidator<DeleteConsumerPermanentlyCommand> validator)
{
    public async Task HandleAsync(
        DeleteConsumerPermanentlyCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var consumer = await repository.GetByNameAsync(command.Name, cancellationToken)
            ?? throw new NotFoundException($"Consumer '{command.Name}' was not found.");

        if (consumer.RevokedAt is null)
        {
            throw new ConflictException("Only revoked consumers can be permanently deleted.");
        }

        await repository.DeleteAsync(consumer, cancellationToken);
    }
}