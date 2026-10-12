using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Domain.Exceptions;
using FluentAssertions;

namespace AIProxyPlus.Domain.Tests.Entities;

public class ConsumerTests
{
    [Fact]
    public void CreateManagedIdentity_WithValidIdentity_CreatesKeylessConsumer()
    {
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();

        var consumer = Consumer.CreateManagedIdentity(Guid.NewGuid(), "payments-worker", tenantId, objectId);

        consumer.ManagedIdentityTenantId.Should().Be(tenantId);
        consumer.ManagedIdentityObjectId.Should().Be(objectId);
        consumer.IsManagedIdentity.Should().BeTrue();
        consumer.ApiKeyHash.Should().BeNull();
        consumer.OidcIssuer.Should().BeNull();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CreateManagedIdentity_WithEmptyIdentityCoordinate_Throws(bool emptyTenant, bool emptyObject)
    {
        var action = () => Consumer.CreateManagedIdentity(
            Guid.NewGuid(),
            "payments-worker",
            emptyTenant ? Guid.Empty : Guid.NewGuid(),
            emptyObject ? Guid.Empty : Guid.NewGuid());

        action.Should().Throw<DomainException>();
    }

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
    public void CreateApiKey_WithBothHashes_SetsBothSlots()
    {
        var consumer = Consumer.CreateApiKey(Guid.NewGuid(), "team-checkout", "hash-1", "hash-2");

        consumer.ApiKeyHash.Should().Be("hash-1");
        consumer.ApiKeyHash2.Should().Be("hash-2");
    }

    [Fact]
    public void Create_WithSingleHash_LeavesSecondSlotUnset()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");

        consumer.ApiKeyHash2.Should().BeNull();
    }

    [Fact]
    public void RotateKey_Primary_UpdatesOnlyPrimaryHash()
    {
        var consumer = Consumer.CreateApiKey(Guid.NewGuid(), "team-checkout", "hash-1", "hash-2");

        consumer.RotateKey(ApiKeySlot.Primary, "hash-1-new");

        consumer.ApiKeyHash.Should().Be("hash-1-new");
        consumer.ApiKeyHash2.Should().Be("hash-2", "rotating one slot must not disturb the other (zero-downtime rotation)");
    }

    [Fact]
    public void RotateKey_Secondary_UpdatesOnlySecondaryHash()
    {
        var consumer = Consumer.CreateApiKey(Guid.NewGuid(), "team-checkout", "hash-1", "hash-2");

        consumer.RotateKey(ApiKeySlot.Secondary, "hash-2-new");

        consumer.ApiKeyHash2.Should().Be("hash-2-new");
        consumer.ApiKeyHash.Should().Be("hash-1");
    }

    [Fact]
    public void RotateKey_Secondary_WhenNeverSet_ConfiguresItForTheFirstTime()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");

        consumer.RotateKey(ApiKeySlot.Secondary, "hash-2");

        consumer.ApiKeyHash2.Should().Be("hash-2");
    }

    [Fact]
    public void RotateKey_WithEmptyHash_Throws()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");

        var act = () => consumer.RotateKey(ApiKeySlot.Primary, "");

        act.Should().Throw<DomainException>().WithMessage("*ApiKeyHash*");
    }

    [Fact]
    public void CreateOidc_SetsExclusiveOidcIdentity()
    {
        var consumer = Consumer.CreateOidc(
            Guid.NewGuid(),
            "proxy-admin",
            "https://identity.example.com/realms/aiproxyplus/",
            "subject-1");

        consumer.ApiKeyHash.Should().BeNull();
        consumer.OidcIssuer.Should().Be("https://identity.example.com/realms/aiproxyplus");
        consumer.OidcSubject.Should().Be("subject-1");
        consumer.IsOidc.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateOidc_WithEmptySubject_Throws(string subject)
    {
        var act = () => Consumer.CreateOidc(
            Guid.NewGuid(),
            "proxy-admin",
            "https://identity.example.com/realms/aiproxyplus",
            subject);

        act.Should().Throw<DomainException>().WithMessage("*OidcSubject*");
    }

    [Fact]
    public void CreateOidc_PreservesOpaqueSubjectExactly()
    {
        var consumer = Consumer.CreateOidc(
            Guid.NewGuid(),
            "proxy-admin",
            "https://identity.example.com/realms/aiproxyplus",
            " subject-1 ");

        consumer.OidcSubject.Should().Be(" subject-1 ");
    }

    [Fact]
    public void RotateKey_ForOidcConsumer_Throws()
    {
        var consumer = Consumer.CreateOidc(
            Guid.NewGuid(),
            "proxy-admin",
            "https://identity.example.com/realms/aiproxyplus",
            "subject-1");

        var act = () => consumer.RotateKey(ApiKeySlot.Primary, "hash-2");

        act.Should().Throw<DomainException>().WithMessage("*do not have API keys*");
    }

    [Fact]
    public void Revoke_DisablesConsumer_And_SetsRevokedAt()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");

        consumer.Revoke();

        consumer.Enabled.Should().BeFalse();
        consumer.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public void Disable_ActiveConsumer_DisablesWithoutRevoking()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");

        consumer.Disable();

        consumer.Enabled.Should().BeFalse();
        consumer.RevokedAt.Should().BeNull();
    }

    [Fact]
    public void Enable_DisabledConsumer_EnablesConsumer()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");
        consumer.Disable();

        consumer.Enable();

        consumer.Enabled.Should().BeTrue();
        consumer.RevokedAt.Should().BeNull();
    }

    [Fact]
    public void Enable_RevokedConsumer_Throws()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");
        consumer.Revoke();

        var act = consumer.Enable;

        act.Should().Throw<DomainException>().WithMessage("*revoked consumer*");
    }

    [Fact]
    public void Revoke_AlreadyRevokedConsumer_PreservesOriginalTimestamp()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");
        var originalTimestamp = DateTimeOffset.UtcNow.AddMinutes(-1);
        consumer.Revoke(originalTimestamp);

        consumer.Revoke();

        consumer.RevokedAt.Should().Be(originalTimestamp);
    }

    [Fact]
    public void NewConsumer_HasUnrestrictedModelAccess_ByDefault()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");

        consumer.HasUnrestrictedModelAccess.Should().BeTrue();
        consumer.CanUseModel(Guid.NewGuid()).Should().BeTrue();
    }

    [Fact]
    public void AllowModel_RestrictsAccessToAllowedModelsOnly()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");
        var allowedModelId = Guid.NewGuid();

        consumer.AllowModel(allowedModelId);

        consumer.HasUnrestrictedModelAccess.Should().BeFalse();
        consumer.CanUseModel(allowedModelId).Should().BeTrue();
        consumer.CanUseModel(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void AllowModel_CalledTwiceForSameModel_IsIdempotent()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");
        var modelId = Guid.NewGuid();

        consumer.AllowModel(modelId);
        consumer.AllowModel(modelId);

        consumer.AllowedModelIds.Should().ContainSingle().Which.Should().Be(modelId);
    }

    [Fact]
    public void RevokeModelAccess_RemovesTheModelFromTheAllowList()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");
        var modelId = Guid.NewGuid();
        consumer.AllowModel(modelId);

        consumer.RevokeModelAccess(modelId);

        consumer.HasUnrestrictedModelAccess.Should().BeTrue();
        consumer.CanUseModel(modelId).Should().BeTrue();
    }

    [Fact]
    public void RevokeModelAccess_ForModelNeverAllowed_IsANoOp()
    {
        var consumer = Consumer.Create(Guid.NewGuid(), "team-checkout", "hash-1");

        var act = () => consumer.RevokeModelAccess(Guid.NewGuid());

        act.Should().NotThrow();
        consumer.HasUnrestrictedModelAccess.Should().BeTrue();
    }
}
