using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Domain.Entities;
using FluentValidation;

namespace AIProxyPlus.Application.Consumers;

public sealed record CreateConsumerCommand(string Name);

public sealed record CreateConsumerResult(Consumer Consumer, string ApiKey1, string ApiKey2);

public sealed class CreateConsumerValidator : AbstractValidator<CreateConsumerCommand>
{
    public CreateConsumerValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateConsumerHandler(
    IConsumerRepository repository,
    IApiKeyGenerator keyGenerator,
    IApiKeyHasher hasher,
    IValidator<CreateConsumerCommand> validator)
{
    public async Task<CreateConsumerResult> HandleAsync(
        CreateConsumerCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        if (await repository.ExistsByNameAsync(command.Name, cancellationToken))
        {
            throw new ConflictException($"A consumer named '{command.Name}' already exists.");
        }

        // Both keys are generated up front (Azure storage-account-key style) — a brand-new consumer already
        // has two live, independently-rotatable keys, not one key plus an optional future second one.
        var plaintextApiKey1 = keyGenerator.Generate();
        var plaintextApiKey2 = keyGenerator.Generate();
        var consumer = Consumer.CreateApiKey(
            Guid.NewGuid(), command.Name, hasher.Hash(plaintextApiKey1), hasher.Hash(plaintextApiKey2));

        await repository.AddAsync(consumer, cancellationToken);
        return new CreateConsumerResult(consumer, plaintextApiKey1, plaintextApiKey2);
    }
}
