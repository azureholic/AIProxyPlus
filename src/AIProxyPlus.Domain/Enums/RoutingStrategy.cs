namespace AIProxyPlus.Domain.Enums;

/// <summary>
/// How endpoints are selected for a <see cref="Entities.Model"/>. Only <see cref="Priority"/> is
/// implemented in Iteration 1; more strategies (e.g. round-robin) are a future extension point.
/// </summary>
public enum RoutingStrategy
{
    Priority
}
