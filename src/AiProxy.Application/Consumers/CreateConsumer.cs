using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentValidation;

namespace AiProxy.Application.Consumers;

public sealed record CreateConsumerCommand(string Name);

public sealed record CreateConsumerResult(Consumer Consumer, string PlaintextApiKey);

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

        var plaintextApiKey = keyGenerator.Generate();
        var consumer = Consumer.Create(Guid.NewGuid(), command.Name, hasher.Hash(plaintextApiKey));

        await repository.AddAsync(consumer, cancellationToken);
        return new CreateConsumerResult(consumer, plaintextApiKey);
    }
}
