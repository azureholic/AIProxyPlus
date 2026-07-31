using AiProxy.Infrastructure.Routing;
using FluentAssertions;

namespace AiProxy.Infrastructure.Tests.Routing;

public class EndpointRoundRobinCounterTests
{
    [Fact]
    public void Next_ForSameDeployment_IncrementsEachCall()
    {
        var counter = new EndpointRoundRobinCounter();
        var deploymentId = Guid.NewGuid();

        counter.Next(deploymentId).Should().Be(0);
        counter.Next(deploymentId).Should().Be(1);
        counter.Next(deploymentId).Should().Be(2);
    }

    [Fact]
    public void Next_ForDifferentDeployments_TracksIndependently()
    {
        var counter = new EndpointRoundRobinCounter();
        var deploymentA = Guid.NewGuid();
        var deploymentB = Guid.NewGuid();

        counter.Next(deploymentA).Should().Be(0);
        counter.Next(deploymentA).Should().Be(1);
        counter.Next(deploymentB).Should().Be(0);
    }
}
