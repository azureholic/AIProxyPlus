namespace AiProxy.Infrastructure.Routing;

public enum RoutingFailureReason
{
    DeploymentNotFound,
    NoHealthyEndpoint,
    StickyEndpointUnavailable,
}

/// <summary>A routing decision that failed before any request was forwarded to a backend.</summary>
public sealed record RoutingFailure(RoutingFailureReason Reason, string Message);
