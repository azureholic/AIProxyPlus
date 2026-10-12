using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Infrastructure.Routing;
using FluentAssertions;

namespace AIProxyPlus.Infrastructure.Tests.Routing;

public class EndpointHealthTrackerTests
{
    [Fact]
    public void GetHealth_ForUnknownEndpoint_ReturnsUnknown() =>
        new EndpointHealthTracker().GetHealth(Guid.NewGuid()).Should().Be(EndpointHealthState.Unknown);

    [Fact]
    public void MarkUnhealthy_ThenGetHealth_WithinCooldown_ReturnsUnhealthy()
    {
        var tracker = new EndpointHealthTracker(TimeSpan.FromSeconds(30));
        var endpointId = Guid.NewGuid();
        var markedAt = DateTimeOffset.UtcNow;

        tracker.MarkUnhealthy(endpointId);

        tracker.GetHealth(endpointId, markedAt.AddSeconds(10)).Should().Be(EndpointHealthState.Unhealthy);
    }

    [Fact]
    public void MarkUnhealthy_ThenGetHealth_AfterCooldownElapses_ReturnsUnknown()
    {
        var tracker = new EndpointHealthTracker(TimeSpan.FromSeconds(30));
        var endpointId = Guid.NewGuid();
        var markedAt = DateTimeOffset.UtcNow;

        tracker.MarkUnhealthy(endpointId);

        tracker.GetHealth(endpointId, markedAt.AddSeconds(31)).Should().Be(EndpointHealthState.Unknown);
    }

    [Fact]
    public void MarkHealthy_ThenGetHealth_ReturnsHealthy_RegardlessOfCooldown()
    {
        var tracker = new EndpointHealthTracker(TimeSpan.FromSeconds(30));
        var endpointId = Guid.NewGuid();

        tracker.MarkHealthy(endpointId);

        tracker.GetHealth(endpointId, DateTimeOffset.UtcNow.AddDays(1)).Should().Be(EndpointHealthState.Healthy);
    }

    [Fact]
    public void GetHealth_WithoutExplicitNow_UsesRealCooldownExpiry()
    {
        var tracker = new EndpointHealthTracker(TimeSpan.FromMilliseconds(1));
        var endpointId = Guid.NewGuid();

        tracker.MarkUnhealthy(endpointId);
        Thread.Sleep(20);

        tracker.GetHealth(endpointId).Should().Be(EndpointHealthState.Unknown);
    }
}
