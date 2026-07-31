using AiProxy.Domain.Entities;
using AiProxy.Domain.Exceptions;
using FluentAssertions;

namespace AiProxy.Domain.Tests.Entities;

public class ConsumerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyName_Throws(string name)
    {
        var act = () => Consumer.Create(Guid.NewGuid(), name, "hash");

        act.Should().Throw<DomainException>().WithMessage("*Name*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyApiKeyHash_Throws(string hash)
    {
        var act = () => Consumer.Create(Guid.NewGuid(), "team-checkout", hash);

        act.Should().Throw<DomainException>().WithMessage("*ApiKeyHash*");
    }

    [Fact]
    public void Create_Succeeds_And_IsEnabled()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");

        consumer.Enabled.Should().BeTrue();
        consumer.RevokedAt.Should().BeNull();
    }

    [Fact]
    public void RotateKey_UpdatesHash()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");

        consumer.RotateKey("hash-2");

        consumer.ApiKeyHash.Should().Be("hash-2");
    }

    [Fact]
    public void RotateKey_WithEmptyHash_Throws()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");

        var act = () => consumer.RotateKey("");

        act.Should().Throw<DomainException>().WithMessage("*ApiKeyHash*");
    }

    [Fact]
    public void Revoke_DisablesConsumer_And_SetsRevokedAt()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");

        consumer.Revoke();

        consumer.Enabled.Should().BeFalse();
        consumer.RevokedAt.Should().NotBeNull();
    }
}
