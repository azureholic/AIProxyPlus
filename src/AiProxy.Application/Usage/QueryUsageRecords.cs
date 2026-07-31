using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using FluentValidation;

namespace AiProxy.Application.Usage;

public sealed class UsageRecordQueryValidator : AbstractValidator<UsageRecordQuery>
{
    public UsageRecordQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 500);
        RuleFor(q => q.From)
            .LessThanOrEqualTo(q => q.To!.Value)
            .When(q => q.From.HasValue && q.To.HasValue)
            .WithMessage("UsageRecordQuery.From must not be after To.");
    }
}

public sealed class QueryUsageRecordsHandler(
    IUsageRecordRepository repository,
    IValidator<UsageRecordQuery> validator)
{
    public async Task<PagedResult<UsageRecord>> HandleAsync(
        UsageRecordQuery query,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);
        return await repository.QueryAsync(query, cancellationToken);
    }
}
