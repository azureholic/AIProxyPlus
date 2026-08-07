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
    ResponsesApi,
    SpeechToText,
    TextToSpeech,

    /// <summary>Anthropic's native Messages API (§9) — distinct from OpenAI-style ChatCompletion passthrough.</summary>
    MessagesApi,

    /// <summary>WebSocket-based realtime/voice API (§9) with connection-scoped affinity.</summary>
    RealtimeVoice
}
