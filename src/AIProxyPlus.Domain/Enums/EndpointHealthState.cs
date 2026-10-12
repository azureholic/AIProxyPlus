namespace AIProxyPlus.Domain.Enums;

/// <summary>Runtime health of an <see cref="Entities.Endpoint"/>, tracked via active/passive health checks.</summary>
public enum EndpointHealthState
{
    Unknown,
    Healthy,
    Unhealthy
}
