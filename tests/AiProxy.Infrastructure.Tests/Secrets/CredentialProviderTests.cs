using Azure.Core;
using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace AiProxy.Infrastructure.Tests.Secrets;

public class CredentialProviderTests
{
    private static Endpoint ApiKeyEndpoint() =>
        Endpoint.Create(
            Guid.NewGuid(), "OpenAI", "https://api.openai.com", ApiFamily.OpenAI, AuthMode.ApiKey,
            secretRef: "endpoints/abc");

    private static Endpoint ManagedIdentityEndpoint() =>
        Endpoint.Create(
            Guid.NewGuid(), "Foundry", "https://foundry.example.com", ApiFamily.AzureOpenAI,
            AuthMode.ManagedIdentity, managedIdentityScope: "scope");

    private static CredentialProvider CreateSut(out ISecretResolver secretResolver, out TokenCredential tokenCredential)
    {
        secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>()).Returns("sk-abc");
        tokenCredential = Substitute.For<TokenCredential>();
        tokenCredential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
            .Returns(new AccessToken("token", DateTimeOffset.UtcNow.AddHours(1)));

        var apiKeyProvider = new ApiKeyCredentialProvider(secretResolver, new MemoryCache(new MemoryCacheOptions()));
        var managedIdentityProvider = new ManagedIdentityCredentialProvider(tokenCredential);
        return new CredentialProvider(apiKeyProvider, managedIdentityProvider);
    }

    [Fact]
    public async Task GetCredentialAsync_ForApiKeyEndpoint_DelegatesToApiKeyProvider()
    {
        var sut = CreateSut(out _, out _);

        var credential = await sut.GetCredentialAsync(ApiKeyEndpoint());

        credential.HeaderName.Should().Be("Authorization");
        credential.HeaderValue.Should().Be("Bearer sk-abc");
    }

    [Fact]
    public async Task GetCredentialAsync_ForManagedIdentityEndpoint_DelegatesToManagedIdentityProvider()
    {
        var sut = CreateSut(out _, out _);

        var credential = await sut.GetCredentialAsync(ManagedIdentityEndpoint());

        credential.HeaderValue.Should().Be("Bearer token");
    }
}
