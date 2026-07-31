using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentValidation;

namespace AiProxy.Application.Consumers;

public sealed record RotateConsumerKeyCommand(string Name);

public sealed record RotateConsumerKeyResult(Consumer Consumer, string PlaintextApiKey);

public sealed class RotateConsumerKeyValidator : AbstractValidator<RotateConsumerKeyCommand>
{
    public RotateConsumerKeyValidator()
    {
        RuleFor(c => c.Name).NotEmpty();
    }
}

public sealed class RotateConsumerKeyHandler(
    IConsumerRepository repository,
    IApiKeyGenerator keyGenerator,
    IApiKeyHasher hasher,
    IValidator<RotateConsumerKeyCommand> validator)
{
    public async Task<RotateConsumerKeyResult> HandleAsync(
        RotateConsumerKeyCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var consumer = await repository.GetByNameAsync(command.Name, cancellationToken)
            ?? throw new NotFoundException($"Consumer '{command.Name}' was not found.");

        var plaintextApiKey = keyGenerator.Generate();
        consumer.RotateKey(hasher.Hash(plaintextApiKey));

        await repository.UpdateAsync(consumer, cancellationToken);
        return new RotateConsumerKeyResult(consumer, plaintextApiKey);
    }
}
