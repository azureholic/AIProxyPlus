using Azure.Core;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Secrets;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Infrastructure.Tests.Secrets;

public class ManagedIdentityCredentialProviderTests
{
    private static ModelEndpoint NewAssignment(
        string? scope = "https://cognitiveservices.azure.com/.default",
        string? tenantId = null)
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "Foundry", "https://foundry.example.com", AuthMode.ManagedIdentity,
            managedIdentityScope: scope, managedIdentityTenantId: tenantId);
        var route = ApiRoute.Create(Guid.NewGuid(), "Foundry OpenAI", Provider.OpenAI, "/openai/v1");
        return ModelEndpoint.Create(Guid.NewGuid(), Guid.NewGuid(), endpoint, route, 1);
    }

    [Fact]
    public async Task GetCredentialAsync_ReturnsBearerTokenFromCredential()
    {
        var tokenCredential = Substitute.For<TokenCredential>();
        tokenCredential
            .GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(new AccessToken("the-token", DateTimeOffset.UtcNow.AddHours(1)));
        var provider = new ManagedIdentityCredentialProvider(tokenCredential);

        var credential = await provider.GetCredentialAsync(NewAssignment());

        credential.HeaderName.Should().Be("Authorization");
        credential.HeaderValue.Should().Be("Bearer the-token");
    }

    [Fact]
    public async Task GetCredentialAsync_RequestsTokenForEndpointScope()
    {
        var tokenCredential = Substitute.For<TokenCredential>();
        tokenCredential
            .GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(new AccessToken("t", DateTimeOffset.UtcNow.AddHours(1)));
        var provider = new ManagedIdentityCredentialProvider(tokenCredential);

        await provider.GetCredentialAsync(NewAssignment("https://my-scope/.default"));

        await tokenCredential.Received(1).GetTokenAsync(
            Arg.Is<TokenRequestContext>(c => c.Scopes.Contains("https://my-scope/.default")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCredentialAsync_WithTenantId_RequestsTokenForEndpointTenant()
    {
        const string tenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";
        var tokenCredential = Substitute.For<TokenCredential>();
        tokenCredential
            .GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(new AccessToken("t", DateTimeOffset.UtcNow.AddHours(1)));
        var provider = new ManagedIdentityCredentialProvider(tokenCredential);

        await provider.GetCredentialAsync(NewAssignment(tenantId: tenantId));

        await tokenCredential.Received(1).GetTokenAsync(
            Arg.Is<TokenRequestContext>(context => context.TenantId == tenantId),
            Arg.Any<CancellationToken>());
    }
}
