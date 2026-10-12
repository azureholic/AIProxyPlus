using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Domain.Enums;

namespace AIProxyPlus.Application.Usage;

/// <summary>Dispatches to the registered <see cref="IUsageExtractor"/> matching a (Provider, OperationType) pair.</summary>
public sealed class UsageExtractorResolver(IEnumerable<IUsageExtractor> extractors) : IUsageExtractorResolver
{
    public IUsageExtractor Resolve(Provider provider, OperationType operationType)
    {
        return extractors.FirstOrDefault(e => e.Provider == provider && e.OperationType == operationType)
            ?? throw new NotFoundException($"No IUsageExtractor is registered for {provider}/{operationType}.");
    }
}
