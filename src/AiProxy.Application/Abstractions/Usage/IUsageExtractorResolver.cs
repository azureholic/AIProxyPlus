using AiProxy.Domain.Enums;

namespace AiProxy.Application.Abstractions;

/// <summary>Picks the right <see cref="IUsageExtractor"/> for a given (Provider, OperationType) pair.</summary>
public interface IUsageExtractorResolver
{
    /// <exception cref="Exceptions.NotFoundException">No extractor is registered for the given pair.</exception>
    IUsageExtractor Resolve(Provider provider, OperationType operationType);
}
