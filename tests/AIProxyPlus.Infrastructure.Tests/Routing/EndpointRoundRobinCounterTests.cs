using AIProxyPlus.Infrastructure.Routing;
using FluentAssertions;

namespace AIProxyPlus.Infrastructure.Tests.Routing;

public class EndpointRoundRobinCounterTests
{
    [Fact]
    public void Next_ForSameModel_IncrementsEachCall()
    {
        var counter = new EndpointRoundRobinCounter();
        var modelId = Guid.NewGuid();

        counter.Next(modelId).Should().Be(0);
        counter.Next(modelId).Should().Be(1);
        counter.Next(modelId).Should().Be(2);
    }

    [Fact]
    public void Next_ForDifferentModels_TracksIndependently()
    {
        var counter = new EndpointRoundRobinCounter();
        var modelA = Guid.NewGuid();
        var modelB = Guid.NewGuid();

        counter.Next(modelA).Should().Be(0);
        counter.Next(modelA).Should().Be(1);
        counter.Next(modelB).Should().Be(0);
    }
}
