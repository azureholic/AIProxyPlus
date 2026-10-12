namespace AIProxyPlus.Domain.Enums;

/// <summary>Identifies one of a <see cref="Entities.Consumer"/>'s two independently-rotatable API key slots
/// (Azure storage-account-key style — rotating one leaves the other valid, architecture.md §8).</summary>
public enum ApiKeySlot
{
    Primary,
    Secondary
}
