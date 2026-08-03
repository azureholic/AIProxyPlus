using System.Diagnostics;

namespace AiProxy.Infrastructure.Observability;

/// <summary>Root <see cref="ActivitySource"/> for custom spans (architecture.md §11): model resolution,
/// secret resolution, backend call, usage extraction — one root `ai_proxy.request` activity per ingress
/// request, these as child spans.</summary>
public static class AiProxyActivitySource
{
    public const string Name = "AiProxy";

    public static readonly ActivitySource Instance = new(Name);
}
