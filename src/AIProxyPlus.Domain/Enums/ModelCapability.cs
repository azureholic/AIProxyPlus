namespace AIProxyPlus.Domain.Enums;

/// <summary>Identifies a Playground experience that can offer a model.</summary>
public enum ModelCapability
{
    Chat,
    ImageGeneration,
    Realtime,
    SpeechToText,
    TextToSpeech
}