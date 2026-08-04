using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentValidation;

namespace AiProxy.Application.Consumers;

public sealed record RevokeConsumerModelCommand(string ConsumerName, string ModelName);

public sealed class RevokeConsumerModelValidator : AbstractValidator<RevokeConsumerModelCommand>
{
    public RevokeConsumerModelValidator()
    {
        RuleFor(c => c.ConsumerName).NotEmpty();
        RuleFor(c => c.ModelName).NotEmpty();
    }
}

public sealed class RevokeConsumerModelHandler(
    IConsumerRepository consumerRepository,
    IModelRepository modelRepository,
    IValidator<RevokeConsumerModelCommand> validator)
{
    public async Task<Consumer> HandleAsync(RevokeConsumerModelCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var consumer = await consumerRepository.GetByNameAsync(command.ConsumerName, cancellationToken)
            ?? throw new NotFoundException($"Consumer '{command.ConsumerName}' was not found.");

        var model = await modelRepository.GetByNameAsync(command.ModelName, cancellationToken)
            ?? throw new NotFoundException($"Model '{command.ModelName}' was not found.");

        consumer.RevokeModelAccess(model.Id);
        await consumerRepository.UpdateAsync(consumer, cancellationToken);

        return consumer;
    }
}
