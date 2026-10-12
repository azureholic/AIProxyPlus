using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using FluentValidation;

namespace AIProxyPlus.Application.Consumers;

public sealed record RotateConsumerKeyCommand(string Name, ApiKeySlot Slot);

public sealed record RotateConsumerKeyResult(Consumer Consumer, string PlaintextApiKey);

public sealed class RotateConsumerKeyValidator : AbstractValidator<RotateConsumerKeyCommand>
{
    public RotateConsumerKeyValidator()
    {
        RuleFor(c => c.Name).NotEmpty();
        RuleFor(c => c.Slot).IsInEnum();
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

        if (consumer.IsOidc)
        {
            throw new ConflictException("OIDC consumers do not have API keys to rotate.");
        }

        var plaintextApiKey = keyGenerator.Generate();
        consumer.RotateKey(command.Slot, hasher.Hash(plaintextApiKey));

        await repository.UpdateAsync(consumer, cancellationToken);
        return new RotateConsumerKeyResult(consumer, plaintextApiKey);
    }
}
