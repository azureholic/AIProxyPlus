using AiProxy.Domain.Enums;

namespace AiProxy.Domain.Entities;

/// <summary>Persists one capability owned by a model.</summary>
public sealed class ModelCapabilityAssignment
{
    public Guid ModelId { get; private set; }
    public ModelCapability Capability { get; private set; }

    private ModelCapabilityAssignment()
    {
    }

    internal ModelCapabilityAssignment(Guid modelId, ModelCapability capability)
    {
        ModelId = modelId;
        Capability = capability;
    }
}