using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;
using FluentAssertions;

namespace AiProxy.Domain.Tests.Entities;

public class EndpointTests
{
    private static Endpoint CreateValidApiKeyEndpoint() =>
        Endpoint.Create(
            id: Guid.NewGuid(),
            name: "OpenAI primary",
            baseUrl: "https://api.openai.com",
            authMode: AuthMode.ApiKey,
            secretRef: "secret/ai-proxy/endpoints/abc");

    [Fact]
    public void Create_WithValidApiKeyAuth_Succeeds()
    {
        var endpoint = CreateValidApiKeyEndpoint();

        endpoint.AuthMode.Should().Be(AuthMode.ApiKey);
        endpoint.Name.Should().Be("OpenAI primary");
        endpoint.PrimarySecretRef.Should().Be("secret/ai-proxy/endpoints/abc");
        endpoint.ActiveCredentialSlot.Should().Be(EndpointCredentialSlot.Primary);
    }

    [Fact]
    public void ActivateCredential_WithConfiguredSecondary_ActivatesSecondaryAndRetainsPrimaryFallback()
    {
        var now = DateTimeOffset.UtcNow;
        var endpoint = CreateValidApiKeyEndpoint();
        endpoint.SetSecretReference(EndpointCredentialSlot.Secondary, "endpoints/secondary/version-1");

        endpoint.ActivateCredential(EndpointCredentialSlot.Secondary, now.AddMinutes(10), now);

        endpoint.ActiveCredentialSlot.Should().Be(EndpointCredentialSlot.Secondary);
        endpoint.FallbackCredentialSlot.Should().Be(EndpointCredentialSlot.Primary);
        endpoint.GetActiveSecretReference().Should().Be("endpoints/secondary/version-1");
        endpoint.GetEligibleFallbackSecretReference(now).Should().Be("secret/ai-proxy/endpoints/abc");
    }

    [Fact]
    public void GetEligibleFallbackSecretReference_AfterExpiry_ReturnsNull()
    {
        var now = DateTimeOffset.UtcNow;
        var endpoint = CreateValidApiKeyEndpoint();
        endpoint.SetSecretReference(EndpointCredentialSlot.Secondary, "endpoints/secondary/version-1");
        endpoint.ActivateCredential(EndpointCredentialSlot.Secondary, now.AddMinutes(1), now);

        var result = endpoint.GetEligibleFallbackSecretReference(now.AddMinutes(2));

        result.Should().BeNull();
    }

    [Fact]
    public void RemoveSecondarySecret_WhenSecondaryIsActive_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        var endpoint = CreateValidApiKeyEndpoint();
        endpoint.SetSecretReference(EndpointCredentialSlot.Secondary, "endpoints/secondary/version-1");
        endpoint.ActivateCredential(EndpointCredentialSlot.Secondary, null, now);

        var act = () => endpoint.RemoveSecondarySecret(now);

        act.Should().Throw<DomainException>().WithMessage("*active credential slot*");
    }

    [Fact]
    public void SetSecretReference_WithManagedIdentityEndpoint_Throws()
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "Foundry", "https://foundry.example.com", AuthMode.ManagedIdentity,
            managedIdentityScope: "https://ai.azure.com/.default");

        var act = () => endpoint.SetSecretReference(EndpointCredentialSlot.Primary, "endpoints/x");

        act.Should().Throw<DomainException>().WithMessage("*ApiKey-mode*");
    }

    [Fact]
    public void Create_WithApiKeyAuthAndNoSecretRef_Throws()
    {
        var act = () => Endpoint.Create(
            Guid.NewGuid(), "OpenAI", "https://api.openai.com", AuthMode.ApiKey, secretRef: null);

        act.Should().Throw<DomainException>().WithMessage("*SecretRef*");
    }

    [Fact]
    public void Create_WithManagedIdentityAuthAndNoScope_Throws()
    {
        var act = () => Endpoint.Create(
            Guid.NewGuid(), "Foundry", "https://foundry.example.com", AuthMode.ManagedIdentity,
            managedIdentityScope: null);

        act.Should().Throw<DomainException>().WithMessage("*ManagedIdentityScope*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyBaseUrl_Throws(string baseUrl)
    {
        var act = () => Endpoint.Create(
            Guid.NewGuid(), "OpenAI", baseUrl, AuthMode.ApiKey, secretRef: "x");

        act.Should().Throw<DomainException>().WithMessage("*BaseUrl*");
    }

    [Fact]
    public void Create_WithEmptyName_Throws()
    {
        var act = () => Endpoint.Create(
            Guid.NewGuid(), "", "https://api.openai.com", AuthMode.ApiKey, secretRef: "x");

        act.Should().Throw<DomainException>().WithMessage("*Name*");
    }

    [Fact]
    public void UpdateTimeoutSeconds_WithValidValue_Succeeds()
    {
        var endpoint = CreateValidApiKeyEndpoint();

        endpoint.UpdateTimeoutSeconds(30);

        endpoint.TimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void UpdateTimeoutSeconds_WithZero_Throws()
    {
        var endpoint = CreateValidApiKeyEndpoint();

        var act = () => endpoint.UpdateTimeoutSeconds(0);

        act.Should().Throw<DomainException>().WithMessage("*TimeoutSeconds*");
    }

    [Fact]
    public void UpdateMaxRetries_WithValidValue_Succeeds()
    {
        var endpoint = CreateValidApiKeyEndpoint();

        endpoint.UpdateMaxRetries(5);

        endpoint.MaxRetries.Should().Be(5);
    }

    [Fact]
    public void UpdateMaxRetries_WithNegativeValue_Throws()
    {
        var endpoint = CreateValidApiKeyEndpoint();

        var act = () => endpoint.UpdateMaxRetries(-1);

        act.Should().Throw<DomainException>().WithMessage("*MaxRetries*");
    }

    [Fact]
    public void UpdateBaseUrl_WithValidValue_UpdatesBaseUrl()
    {
        var endpoint = CreateValidApiKeyEndpoint();

        endpoint.UpdateBaseUrl("https://updated.openai.azure.com");

        endpoint.BaseUrl.Should().Be("https://updated.openai.azure.com");
    }

    [Fact]
    public void UpdateManagedIdentityScope_WithManagedIdentityEndpoint_UpdatesScope()
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "Foundry", "https://foundry.example.com", AuthMode.ManagedIdentity,
            managedIdentityScope: "https://cognitiveservices.azure.com/.default");

        endpoint.UpdateManagedIdentityScope("https://ai.azure.com/.default");

        endpoint.ManagedIdentityScope.Should().Be("https://ai.azure.com/.default");
    }

    [Fact]
    public void Create_WithValidManagedIdentityTenantId_Succeeds()
    {
        const string tenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";

        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "Foundry", "https://foundry.example.com", AuthMode.ManagedIdentity,
            managedIdentityScope: "https://ai.azure.com/.default",
            managedIdentityTenantId: tenantId);

        endpoint.ManagedIdentityTenantId.Should().Be(tenantId);
    }

    [Fact]
    public void Create_WithInvalidManagedIdentityTenantId_Throws()
    {
        var act = () => Endpoint.Create(
            Guid.NewGuid(), "Foundry", "https://foundry.example.com", AuthMode.ManagedIdentity,
            managedIdentityScope: "https://ai.azure.com/.default",
            managedIdentityTenantId: "not-a-guid");

        act.Should().Throw<DomainException>().WithMessage("*ManagedIdentityTenantId*");
    }

    [Fact]
    public void UpdateManagedIdentityTenantId_WithManagedIdentityEndpoint_UpdatesTenantId()
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "Foundry", "https://foundry.example.com", AuthMode.ManagedIdentity,
            managedIdentityScope: "https://ai.azure.com/.default");

        endpoint.UpdateManagedIdentityTenantId("72f988bf-86f1-41af-91ab-2d7cd011db47");

        endpoint.ManagedIdentityTenantId.Should().Be("72f988bf-86f1-41af-91ab-2d7cd011db47");
    }

    [Fact]
    public void UpdateManagedIdentityTenantId_WithEmptyValue_ClearsTenantId()
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "Foundry", "https://foundry.example.com", AuthMode.ManagedIdentity,
            managedIdentityScope: "https://ai.azure.com/.default",
            managedIdentityTenantId: "72f988bf-86f1-41af-91ab-2d7cd011db47");

        endpoint.UpdateManagedIdentityTenantId(string.Empty);

        endpoint.ManagedIdentityTenantId.Should().BeNull();
    }
}
