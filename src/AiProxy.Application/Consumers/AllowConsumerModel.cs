using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentValidation;

namespace AiProxy.Application.Consumers;

public sealed record AllowConsumerModelCommand(string ConsumerName, string ModelName);

public sealed class AllowConsumerModelValidator : AbstractValidator<AllowConsumerModelCommand>
{
    public AllowConsumerModelValidator()
    {
        RuleFor(c => c.ConsumerName).NotEmpty();
        RuleFor(c => c.ModelName).NotEmpty();
    }
}

public sealed class AllowConsumerModelHandler(
    IConsumerRepository consumerRepository,
    IModelRepository modelRepository,
    IValidator<AllowConsumerModelCommand> validator)
{
    public async Task<Consumer> HandleAsync(AllowConsumerModelCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var consumer = await consumerRepository.GetByNameAsync(command.ConsumerName, cancellationToken)
            ?? throw new NotFoundException($"Consumer '{command.ConsumerName}' was not found.");

        var model = await modelRepository.GetByNameAsync(command.ModelName, cancellationToken)
            ?? throw new NotFoundException($"Model '{command.ModelName}' was not found.");

        consumer.AllowModel(model.Id);
        await consumerRepository.UpdateAsync(consumer, cancellationToken);

        return consumer;
    }
}
