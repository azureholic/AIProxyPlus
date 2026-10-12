using System.Diagnostics;

namespace AIProxyPlus.Infrastructure.Observability;

/// <summary>Root <see cref="ActivitySource"/> for custom spans (architecture.md §11): model resolution,
/// secret resolution, backend call, usage extraction — one root `aiproxyplus.request` activity per ingress
/// request, these as child spans.</summary>
public static class AIProxyPlusActivitySource
{
    public const string Name = "AIProxyPlus";

    public static readonly ActivitySource Instance = new(Name);
}
