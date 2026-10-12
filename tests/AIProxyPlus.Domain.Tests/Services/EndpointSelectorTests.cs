using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Domain.Services;
using FluentAssertions;

namespace AIProxyPlus.Domain.Tests.Services;

public class EndpointSelectorTests
{
    private static ModelEndpoint Healthy(Guid modelId, int priority)
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "Endpoint", "https://api.example.com", AuthMode.ApiKey, secretRef: "secret/x");
        var route = ApiRoute.Create(Guid.NewGuid(), "Native OpenAI", Provider.OpenAI, "/v1");
        var assignment = ModelEndpoint.Create(Guid.NewGuid(), modelId, endpoint, route, priority);
        assignment.MarkHealthy();
        return assignment;
    }

    private static ModelEndpoint Unhealthy(Guid modelId, int priority)
    {
        var endpoint = Healthy(modelId, priority);
        endpoint.MarkUnhealthy();
        return endpoint;
    }

    [Fact]
    public void SelectEndpoint_NoEndpoints_ReturnsFailure()
    {
        var result = EndpointSelector.SelectEndpoint([]);

        result.IsSuccess.Should().BeFalse();
        result.SelectedEndpoint.Should().BeNull();
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SelectEndpoint_SingleHealthyEndpoint_ReturnsIt()
    {
        var modelId = Guid.NewGuid();
        var endpoint = Healthy(modelId, priority: 1);

        var result = EndpointSelector.SelectEndpoint([endpoint]);

        result.IsSuccess.Should().BeTrue();
        result.SelectedEndpoint.Should().Be(endpoint);
    }

    [Fact]
    public void SelectEndpoint_MultipleHealthy_ReturnsLowestPriority()
    {
        var modelId = Guid.NewGuid();
        var primary = Healthy(modelId, priority: 1);
        var secondary = Healthy(modelId, priority: 2);

        var result = EndpointSelector.SelectEndpoint([secondary, primary]);

        result.IsSuccess.Should().BeTrue();
        result.SelectedEndpoint.Should().Be(primary);
    }

    [Fact]
    public void SelectEndpoint_AllUnhealthy_ReturnsFailure()
    {
        var modelId = Guid.NewGuid();
        var endpoints = new[] { Unhealthy(modelId, 1), Unhealthy(modelId, 2) };

        var result = EndpointSelector.SelectEndpoint(endpoints);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("unhealthy");
    }

    [Fact]
    public void SelectEndpoint_UnhealthyPrimary_FallsBackToHealthySecondary()
    {
        var modelId = Guid.NewGuid();
        var primary = Unhealthy(modelId, priority: 1);
        var secondary = Healthy(modelId, priority: 2);

        var result = EndpointSelector.SelectEndpoint([primary, secondary]);

        result.IsSuccess.Should().BeTrue();
        result.SelectedEndpoint.Should().Be(secondary);
    }

    [Fact]
    public void SelectEndpoint_ExcludedEndpointIds_SkipsThem()
    {
        var modelId = Guid.NewGuid();
        var primary = Healthy(modelId, priority: 1);
        var secondary = Healthy(modelId, priority: 2);

        var result = EndpointSelector.SelectEndpoint([primary, secondary], excludedEndpointIds: new HashSet<Guid> { primary.EndpointId });

        result.IsSuccess.Should().BeTrue();
        result.SelectedEndpoint.Should().Be(secondary);
    }

    [Fact]
    public void SelectEndpoint_DisabledEndpoint_IsIgnored()
    {
        var modelId = Guid.NewGuid();
        var primary = Healthy(modelId, priority: 1);
        primary.Disable();
        var secondary = Healthy(modelId, priority: 2);

        var result = EndpointSelector.SelectEndpoint([primary, secondary]);

        result.IsSuccess.Should().BeTrue();
        result.SelectedEndpoint.Should().Be(secondary);
    }

    [Fact]
    public void SelectEndpoint_StickyEndpointHealthy_ReturnsItEvenIfNotLowestPriority()
    {
        var modelId = Guid.NewGuid();
        var primary = Healthy(modelId, priority: 1);
        var sticky = Healthy(modelId, priority: 2);

        var result = EndpointSelector.SelectEndpoint([primary, sticky], stickyAssignmentId: sticky.Id);

        result.IsSuccess.Should().BeTrue();
        result.SelectedEndpoint.Should().Be(sticky);
    }

    [Fact]
    public void SelectEndpoint_StickyEndpointUnhealthy_FailsFastWithoutFallback()
    {
        var modelId = Guid.NewGuid();
        var sticky = Unhealthy(modelId, priority: 1);
        var otherHealthy = Healthy(modelId, priority: 2);

        var result = EndpointSelector.SelectEndpoint([sticky, otherHealthy], stickyAssignmentId: sticky.Id);

        result.IsSuccess.Should().BeFalse();
        result.SelectedEndpoint.Should().BeNull();
        result.FailureReason.Should().Contain("unhealthy");
    }

    [Fact]
    public void SelectEndpoint_StickyEndpointNotFound_ReturnsFailure()
    {
        var modelId = Guid.NewGuid();
        var onlyEndpoint = Healthy(modelId, priority: 1);

        var result = EndpointSelector.SelectEndpoint([onlyEndpoint], stickyAssignmentId: Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void SelectEndpoint_StickyEndpointDisabled_ReturnsFailure()
    {
        var modelId = Guid.NewGuid();
        var sticky = Healthy(modelId, priority: 1);
        sticky.Disable();

        var result = EndpointSelector.SelectEndpoint([sticky], stickyAssignmentId: sticky.Id);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void SelectEndpoint_SamePriorityTier_RoundRobinsAcrossCounterValues()
    {
        var modelId = Guid.NewGuid();
        var a = Healthy(modelId, priority: 1);
        var b = Healthy(modelId, priority: 1);
        var endpoints = new[] { a, b };
        var tier = new[] { a, b }.OrderBy(e => e.Id).ToList();

        for (var counter = 0; counter < 6; counter++)
        {
            var result = EndpointSelector.SelectEndpoint(endpoints, roundRobinCounter: counter);

            result.IsSuccess.Should().BeTrue();
            result.SelectedEndpoint.Should().Be(tier[counter % tier.Count]);
        }
    }

    [Fact]
    public void SelectEndpoint_SamePriorityTier_NegativeCounter_StillProducesAValidIndex()
    {
        var modelId = Guid.NewGuid();
        var a = Healthy(modelId, priority: 1);
        var b = Healthy(modelId, priority: 1);

        var result = EndpointSelector.SelectEndpoint([a, b], roundRobinCounter: -1);

        result.IsSuccess.Should().BeTrue();
        result.SelectedEndpoint.Should().BeOneOf(a, b);
    }

    [Fact]
    public void SelectEndpoint_RoundRobinTier_NeverPicksALowerPriorityEndpoint()
    {
        var modelId = Guid.NewGuid();
        var a = Healthy(modelId, priority: 1);
        var b = Healthy(modelId, priority: 1);
        var fallback = Healthy(modelId, priority: 2);

        for (var counter = 0; counter < 4; counter++)
        {
            var result = EndpointSelector.SelectEndpoint([a, b, fallback], roundRobinCounter: counter);

            result.SelectedEndpoint.Should().BeOneOf(a, b);
        }
    }

    [Fact]
    public void SelectEndpoint_RoundRobinTier_ExcludingOneMember_AlwaysPicksTheSurvivor()
    {
        var modelId = Guid.NewGuid();
        var a = Healthy(modelId, priority: 1);
        var b = Healthy(modelId, priority: 1);

        for (var counter = 0; counter < 4; counter++)
        {
            var result = EndpointSelector.SelectEndpoint(
                [a, b], excludedEndpointIds: new HashSet<Guid> { a.EndpointId }, roundRobinCounter: counter);

            result.SelectedEndpoint.Should().Be(b);
        }
    }
}
