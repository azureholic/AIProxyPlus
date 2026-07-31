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
}
