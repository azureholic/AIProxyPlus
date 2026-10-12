using Azure.Core;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace AIProxyPlus.Infrastructure.Tests.Secrets;

public class CredentialProviderTests
{
    private static ModelEndpoint ApiKeyEndpoint() => CreateAssignment(
        Endpoint.Create(Guid.NewGuid(), "OpenAI", "https://api.openai.com", AuthMode.ApiKey, "endpoints/abc"));

    private static ModelEndpoint ManagedIdentityEndpoint() => CreateAssignment(
        Endpoint.Create(
            Guid.NewGuid(), "Foundry", "https://foundry.example.com", AuthMode.ManagedIdentity,
            managedIdentityScope: "scope"));

    private static ModelEndpoint CreateAssignment(Endpoint endpoint) => ModelEndpoint.Create(
        Guid.NewGuid(), Guid.NewGuid(), endpoint,
        ApiRoute.Create(Guid.NewGuid(), "OpenAI", Provider.OpenAI, "/v1"), 1);

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
