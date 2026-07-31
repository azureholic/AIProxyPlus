namespace AiProxy.Domain.Enums;

/// <summary>
/// The kind of inference operation a request represents. Determines which <c>IUsageExtractor</c> is used
/// (architecture.md §4/§10) and is extensible — new operation types can be added without touching routing.
/// </summary>
public enum OperationType
{
    ChatCompletion,
    Completion,
    Embedding,
    ImageGeneration,
    ImageEdit,
    ImageVariation,
    ResponsesApi
}
