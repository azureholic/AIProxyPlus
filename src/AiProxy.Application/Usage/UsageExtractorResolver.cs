using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Enums;

namespace AiProxy.Application.Usage;

/// <summary>Dispatches to the registered <see cref="IUsageExtractor"/> matching an (ApiFamily, OperationType) pair.</summary>
public sealed class UsageExtractorResolver(IEnumerable<IUsageExtractor> extractors) : IUsageExtractorResolver
{
    public IUsageExtractor Resolve(ApiFamily apiFamily, OperationType operationType)
    {
        return extractors.FirstOrDefault(e => e.ApiFamily == apiFamily && e.OperationType == operationType)
            ?? throw new NotFoundException($"No IUsageExtractor is registered for {apiFamily}/{operationType}.");
    }
}
