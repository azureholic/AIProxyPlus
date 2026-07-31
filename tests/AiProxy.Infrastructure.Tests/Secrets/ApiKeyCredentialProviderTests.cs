using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace AiProxy.Infrastructure.Tests.Secrets;

public class ApiKeyCredentialProviderTests
{
    private static Endpoint NewEndpoint(ApiFamily apiFamily, string? secretRef = "endpoints/abc") =>
        Endpoint.Create(
            Guid.NewGuid(), Guid.NewGuid(), "https://api.example.com", apiFamily, 1, AuthMode.ApiKey, secretRef: secretRef);

    [Theory]
    [InlineData(ApiFamily.OpenAI, "Authorization", "Bearer sk-abc")]
    [InlineData(ApiFamily.OpenAICompatible, "Authorization", "Bearer sk-abc")]
    [InlineData(ApiFamily.AzureOpenAI, "api-key", "sk-abc")]
    [InlineData(ApiFamily.Anthropic, "x-api-key", "sk-abc")]
    public async Task GetCredentialAsync_BuildsHeader_PerApiFamilyConvention(ApiFamily apiFamily, string expectedHeader, string expectedValue)
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>()).Returns("sk-abc");
        var provider = new ApiKeyCredentialProvider(secretResolver, new MemoryCache(new MemoryCacheOptions()));

        var credential = await provider.GetCredentialAsync(NewEndpoint(apiFamily));

        credential.HeaderName.Should().Be(expectedHeader);
        credential.HeaderValue.Should().Be(expectedValue);
    }

    [Fact]
    public async Task GetCredentialAsync_CalledTwiceWithinTtl_OnlyResolvesOnce()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>()).Returns("sk-abc");
        var provider = new ApiKeyCredentialProvider(secretResolver, new MemoryCache(new MemoryCacheOptions()), TimeSpan.FromMinutes(5));
        var endpoint = NewEndpoint(ApiFamily.OpenAI);

        await provider.GetCredentialAsync(endpoint);
        await provider.GetCredentialAsync(endpoint);

        await secretResolver.Received(1).TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCredentialAsync_AfterTtlExpires_ResolvesAgain()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>()).Returns("sk-abc");
        var provider = new ApiKeyCredentialProvider(secretResolver, new MemoryCache(new MemoryCacheOptions()), TimeSpan.FromMilliseconds(50));
        var endpoint = NewEndpoint(ApiFamily.OpenAI);

        await provider.GetCredentialAsync(endpoint);
        await Task.Delay(150);
        await provider.GetCredentialAsync(endpoint);

        await secretResolver.Received(2).TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCredentialAsync_WhenSecretMissingInVault_Throws()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>()).Returns((string?)null);
        var provider = new ApiKeyCredentialProvider(secretResolver, new MemoryCache(new MemoryCacheOptions()));

        var act = () => provider.GetCredentialAsync(NewEndpoint(ApiFamily.OpenAI));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
