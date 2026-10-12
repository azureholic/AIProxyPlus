namespace AIProxyPlus.Domain.Entities;

/// <summary>Grants a consumer permission to invoke one specific model (architecture.md §13). A consumer with
/// no rows here has unrestricted access — see <see cref="Consumer.HasUnrestrictedModelAccess"/>.</summary>
public sealed class ConsumerAllowedModel
{
    public Guid ConsumerId { get; private set; }
    public Guid ModelId { get; private set; }

    private ConsumerAllowedModel()
    {
    }

    internal ConsumerAllowedModel(Guid consumerId, Guid modelId)
    {
        ConsumerId = consumerId;
        ModelId = modelId;
    }
}
