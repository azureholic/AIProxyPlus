using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;
using FluentAssertions;

namespace AiProxy.Domain.Tests.Entities;

public class EndpointTests
{
    private static Endpoint CreateValidApiKeyEndpoint(Guid? modelDeploymentId = null, int priority = 1) =>
        Endpoint.Create(
            id: Guid.NewGuid(),
            modelDeploymentId: modelDeploymentId ?? Guid.NewGuid(),
            baseUrl: "https://api.openai.com",
            apiFamily: ApiFamily.OpenAI,
            priority: priority,
            authMode: AuthMode.ApiKey,
            secretRef: "secret/ai-proxy/endpoints/abc");

    [Fact]
    public void Create_WithValidApiKeyAuth_Succeeds()
    {
        var endpoint = CreateValidApiKeyEndpoint();

        endpoint.AuthMode.Should().Be(AuthMode.ApiKey);
        endpoint.Health.Should().Be(EndpointHealthState.Unknown);
        endpoint.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Create_WithApiKeyAuthAndNoSecretRef_Throws()
    {
        var act = () => Endpoint.Create(
            Guid.NewGuid(), Guid.NewGuid(), "https://api.openai.com", ApiFamily.OpenAI, 1, AuthMode.ApiKey, secretRef: null);

        act.Should().Throw<DomainException>().WithMessage("*SecretRef*");
    }

    [Fact]
    public void Create_WithManagedIdentityAuthAndNoScope_Throws()
    {
        var act = () => Endpoint.Create(
            Guid.NewGuid(), Guid.NewGuid(), "https://foundry.example.com", ApiFamily.AzureOpenAI, 1,
            AuthMode.ManagedIdentity, managedIdentityScope: null);

        act.Should().Throw<DomainException>().WithMessage("*ManagedIdentityScope*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyBaseUrl_Throws(string baseUrl)
    {
        var act = () => Endpoint.Create(
            Guid.NewGuid(), Guid.NewGuid(), baseUrl, ApiFamily.OpenAI, 1, AuthMode.ApiKey, secretRef: "x");

        act.Should().Throw<DomainException>().WithMessage("*BaseUrl*");
    }

    [Fact]
    public void Create_WithPriorityLessThanOne_Throws()
    {
        var act = () => Endpoint.Create(
            Guid.NewGuid(), Guid.NewGuid(), "https://api.openai.com", ApiFamily.OpenAI, 0, AuthMode.ApiKey, secretRef: "x");

        act.Should().Throw<DomainException>().WithMessage("*Priority*");
    }

    [Fact]
    public void MarkUnhealthy_Then_MarkHealthy_UpdatesHealth()
    {
        var endpoint = CreateValidApiKeyEndpoint();

        endpoint.MarkUnhealthy();
        endpoint.Health.Should().Be(EndpointHealthState.Unhealthy);

        endpoint.MarkHealthy();
        endpoint.Health.Should().Be(EndpointHealthState.Healthy);
    }

    [Fact]
    public void Disable_Then_Enable_TogglesEnabled()
    {
        var endpoint = CreateValidApiKeyEndpoint();

        endpoint.Disable();
        endpoint.Enabled.Should().BeFalse();

        endpoint.Enable();
        endpoint.Enabled.Should().BeTrue();
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
            Guid.NewGuid(), Guid.NewGuid(), "https://foundry.example.com", ApiFamily.AzureOpenAI, 1,
            AuthMode.ManagedIdentity, managedIdentityScope: "https://cognitiveservices.azure.com/.default");

        endpoint.UpdateManagedIdentityScope("https://ai.azure.com/.default");

        endpoint.ManagedIdentityScope.Should().Be("https://ai.azure.com/.default");
    }

    [Fact]
    public void Create_WithValidManagedIdentityTenantId_Succeeds()
    {
        const string tenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";

        var endpoint = Endpoint.Create(
            Guid.NewGuid(), Guid.NewGuid(), "https://foundry.example.com", ApiFamily.AzureOpenAI, 1,
            AuthMode.ManagedIdentity, managedIdentityScope: "https://ai.azure.com/.default",
            managedIdentityTenantId: tenantId);

        endpoint.ManagedIdentityTenantId.Should().Be(tenantId);
    }

    [Fact]
    public void Create_WithInvalidManagedIdentityTenantId_Throws()
    {
        var act = () => Endpoint.Create(
            Guid.NewGuid(), Guid.NewGuid(), "https://foundry.example.com", ApiFamily.AzureOpenAI, 1,
            AuthMode.ManagedIdentity, managedIdentityScope: "https://ai.azure.com/.default",
            managedIdentityTenantId: "not-a-guid");

        act.Should().Throw<DomainException>().WithMessage("*ManagedIdentityTenantId*");
    }

    [Fact]
    public void UpdateManagedIdentityTenantId_WithManagedIdentityEndpoint_UpdatesTenantId()
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), Guid.NewGuid(), "https://foundry.example.com", ApiFamily.AzureOpenAI, 1,
            AuthMode.ManagedIdentity, managedIdentityScope: "https://ai.azure.com/.default");

        endpoint.UpdateManagedIdentityTenantId("72f988bf-86f1-41af-91ab-2d7cd011db47");

        endpoint.ManagedIdentityTenantId.Should().Be("72f988bf-86f1-41af-91ab-2d7cd011db47");
    }

    [Fact]
    public void UpdateManagedIdentityTenantId_WithEmptyValue_ClearsTenantId()
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), Guid.NewGuid(), "https://foundry.example.com", ApiFamily.AzureOpenAI, 1,
            AuthMode.ManagedIdentity, managedIdentityScope: "https://ai.azure.com/.default",
            managedIdentityTenantId: "72f988bf-86f1-41af-91ab-2d7cd011db47");

        endpoint.UpdateManagedIdentityTenantId(string.Empty);

        endpoint.ManagedIdentityTenantId.Should().BeNull();
    }
}
