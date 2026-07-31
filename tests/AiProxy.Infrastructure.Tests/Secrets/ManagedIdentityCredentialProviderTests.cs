using Azure.Core;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Secrets;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Infrastructure.Tests.Secrets;

public class ManagedIdentityCredentialProviderTests
{
    private static Endpoint NewEndpoint(string? scope = "https://cognitiveservices.azure.com/.default") =>
        Endpoint.Create(
            Guid.NewGuid(), Guid.NewGuid(), "https://foundry.example.com", ApiFamily.AzureOpenAI, 1,
            AuthMode.ManagedIdentity, managedIdentityScope: scope);

    [Fact]
    public async Task GetCredentialAsync_ReturnsBearerTokenFromCredential()
    {
        var tokenCredential = Substitute.For<TokenCredential>();
        tokenCredential
            .GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(new AccessToken("the-token", DateTimeOffset.UtcNow.AddHours(1)));
        var provider = new ManagedIdentityCredentialProvider(tokenCredential);

        var credential = await provider.GetCredentialAsync(NewEndpoint());

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

        await provider.GetCredentialAsync(NewEndpoint("https://my-scope/.default"));

        await tokenCredential.Received(1).GetTokenAsync(
            Arg.Is<TokenRequestContext>(c => c.Scopes.Contains("https://my-scope/.default")),
            Arg.Any<CancellationToken>());
    }
}
