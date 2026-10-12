using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Exceptions;
using FluentAssertions;

namespace AIProxyPlus.Domain.Tests.Entities;

public class SessionAffinityEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_SetsExpiryFromTtl()
    {
        var assignmentId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();

        var entry = SessionAffinityEntry.Create(
            "resp_abc123", Guid.NewGuid(), assignmentId, endpointId, TimeSpan.FromMinutes(30), Now);

        entry.ModelEndpointId.Should().Be(assignmentId);
        entry.EndpointId.Should().Be(endpointId);
        entry.LastUsedAt.Should().Be(Now);
        entry.ExpiresAt.Should().Be(Now.AddMinutes(30));
    }

    [Fact]
    public void Create_WithNonPositiveTtl_Throws()
    {
        var act = () => SessionAffinityEntry.Create(
            "session-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TimeSpan.Zero, Now);

        act.Should().Throw<DomainException>().WithMessage("*TTL*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptySessionId_Throws(string sessionId)
    {
        var act = () => SessionAffinityEntry.Create(
            sessionId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(30), Now);

        act.Should().Throw<DomainException>().WithMessage("*SessionId*");
    }

    [Fact]
    public void Touch_ExtendsExpiry()
    {
        var entry = SessionAffinityEntry.Create(
            "session-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(30), Now);

        entry.Touch(TimeSpan.FromMinutes(30), Now.AddMinutes(10));

        entry.LastUsedAt.Should().Be(Now.AddMinutes(10));
        entry.ExpiresAt.Should().Be(Now.AddMinutes(40));
    }

    [Fact]
    public void IsExpired_BeforeExpiry_IsFalse()
    {
        var entry = SessionAffinityEntry.Create(
            "session-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(30), Now);

        entry.IsExpired(Now.AddMinutes(29)).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_AfterExpiry_IsTrue()
    {
        var entry = SessionAffinityEntry.Create(
            "session-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(30), Now);

        entry.IsExpired(Now.AddMinutes(31)).Should().BeTrue();
    }
}
