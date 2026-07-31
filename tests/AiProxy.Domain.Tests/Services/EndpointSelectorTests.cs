using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Domain.Services;
using FluentAssertions;

namespace AiProxy.Domain.Tests.Services;

public class EndpointSelectorTests
{
    private static Endpoint Healthy(Guid deploymentId, int priority)
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), deploymentId, "https://api.example.com", ApiFamily.OpenAI, priority,
            AuthMode.ApiKey, secretRef: "secret/x");
        endpoint.MarkHealthy();
        return endpoint;
    }

    private static Endpoint Unhealthy(Guid deploymentId, int priority)
    {
        var endpoint = Healthy(deploymentId, priority);
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
        var deploymentId = Guid.NewGuid();
        var endpoint = Healthy(deploymentId, priority: 1);

        var result = EndpointSelector.SelectEndpoint([endpoint]);

        result.IsSuccess.Should().BeTrue();
        result.SelectedEndpoint.Should().Be(endpoint);
    }

    [Fact]
    public void SelectEndpoint_MultipleHealthy_ReturnsLowestPriority()
    {
        var deploymentId = Guid.NewGuid();
        var primary = Healthy(deploymentId, priority: 1);
        var secondary = Healthy(deploymentId, priority: 2);

        var result = EndpointSelector.SelectEndpoint([secondary, primary]);

        result.IsSuccess.Should().BeTrue();
        result.SelectedEndpoint.Should().Be(primary);
    }

    [Fact]
    public void SelectEndpoint_AllUnhealthy_ReturnsFailure()
    {
        var deploymentId = Guid.NewGuid();
        var endpoints = new[] { Unhealthy(deploymentId, 1), Unhealthy(deploymentId, 2) };

        var result = EndpointSelector.SelectEndpoint(endpoints);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("unhealthy");
    }

    [Fact]
    public void SelectEndpoint_UnhealthyPrimary_FallsBackToHealthySecondary()
    {
        var deploymentId = Guid.NewGuid();
        var primary = Unhealthy(deploymentId, priority: 1);
        var secondary = Healthy(deploymentId, priority: 2);

        var result = EndpointSelector.SelectEndpoint([primary, secondary]);

        result.IsSuccess.Should().BeTrue();
        result.SelectedEndpoint.Should().Be(secondary);
    }

    [Fact]
    public void SelectEndpoint_ExcludedEndpointIds_SkipsThem()
    {
        var deploymentId = Guid.NewGuid();
        var primary = Healthy(deploymentId, priority: 1);
        var secondary = Healthy(deploymentId, priority: 2);

        var result = EndpointSelector.SelectEndpoint([primary, secondary], excludedEndpointIds: new HashSet<Guid> { primary.Id });

        result.IsSuccess.Should().BeTrue();
        result.SelectedEndpoint.Should().Be(secondary);
    }

    [Fact]
    public void SelectEndpoint_DisabledEndpoint_IsIgnored()
    {
        var deploymentId = Guid.NewGuid();
        var primary = Healthy(deploymentId, priority: 1);
        primary.Disable();
        var secondary = Healthy(deploymentId, priority: 2);

        var result = EndpointSelector.SelectEndpoint([primary, secondary]);

        result.IsSuccess.Should().BeTrue();
        result.SelectedEndpoint.Should().Be(secondary);
    }

    [Fact]
    public void SelectEndpoint_StickyEndpointHealthy_ReturnsItEvenIfNotLowestPriority()
    {
        var deploymentId = Guid.NewGuid();
        var primary = Healthy(deploymentId, priority: 1);
        var sticky = Healthy(deploymentId, priority: 2);

        var result = EndpointSelector.SelectEndpoint([primary, sticky], stickyEndpointId: sticky.Id);

        result.IsSuccess.Should().BeTrue();
        result.SelectedEndpoint.Should().Be(sticky);
    }

    [Fact]
    public void SelectEndpoint_StickyEndpointUnhealthy_FailsFastWithoutFallback()
    {
        var deploymentId = Guid.NewGuid();
        var sticky = Unhealthy(deploymentId, priority: 1);
        var otherHealthy = Healthy(deploymentId, priority: 2);

        var result = EndpointSelector.SelectEndpoint([sticky, otherHealthy], stickyEndpointId: sticky.Id);

        result.IsSuccess.Should().BeFalse();
        result.SelectedEndpoint.Should().BeNull();
        result.FailureReason.Should().Contain("unhealthy");
    }

    [Fact]
    public void SelectEndpoint_StickyEndpointNotFound_ReturnsFailure()
    {
        var deploymentId = Guid.NewGuid();
        var onlyEndpoint = Healthy(deploymentId, priority: 1);

        var result = EndpointSelector.SelectEndpoint([onlyEndpoint], stickyEndpointId: Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void SelectEndpoint_StickyEndpointDisabled_ReturnsFailure()
    {
        var deploymentId = Guid.NewGuid();
        var sticky = Healthy(deploymentId, priority: 1);
        sticky.Disable();

        var result = EndpointSelector.SelectEndpoint([sticky], stickyEndpointId: sticky.Id);

        result.IsSuccess.Should().BeFalse();
    }
}
