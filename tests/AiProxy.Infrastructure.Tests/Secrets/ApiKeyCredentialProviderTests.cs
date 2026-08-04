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
    private static ModelEndpoint NewAssignment(Provider provider, string? secretRef = "endpoints/abc") =>
        ModelEndpoint.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Endpoint.Create(Guid.NewGuid(), "API endpoint", "https://api.example.com", AuthMode.ApiKey, secretRef),
            ApiRoute.Create(Guid.NewGuid(), $"{provider} route", provider, "/v1"),
            1);

    [Theory]
    [InlineData(Provider.OpenAI, "Authorization", "Bearer sk-abc")]
    [InlineData(Provider.MicrosoftAI, "api-key", "sk-abc")]
    [InlineData(Provider.Anthropic, "x-api-key", "sk-abc")]
    public async Task GetCredentialAsync_BuildsHeader_PerProviderConvention(Provider routeProvider, string expectedHeader, string expectedValue)
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>()).Returns("sk-abc");
        var provider = new ApiKeyCredentialProvider(secretResolver, new MemoryCache(new MemoryCacheOptions()));

        var credential = await provider.GetCredentialAsync(NewAssignment(routeProvider));

        credential.HeaderName.Should().Be(expectedHeader);
        credential.HeaderValue.Should().Be(expectedValue);
    }

    [Fact]
    public async Task GetCredentialAsync_CalledTwiceWithinTtl_OnlyResolvesOnce()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>()).Returns("sk-abc");
        var provider = new ApiKeyCredentialProvider(secretResolver, new MemoryCache(new MemoryCacheOptions()), TimeSpan.FromMinutes(5));
        var endpoint = NewAssignment(Provider.OpenAI);

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
        var endpoint = NewAssignment(Provider.OpenAI);

        await provider.GetCredentialAsync(endpoint);
        await Task.Delay(150);
        await provider.GetCredentialAsync(endpoint);

        await secretResolver.Received(2).TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalidate_RemovesCachedSecret()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>())
            .Returns("sk-old", "sk-new");
        var provider = new ApiKeyCredentialProvider(
            secretResolver, new MemoryCache(new MemoryCacheOptions()), TimeSpan.FromMinutes(5));
        var endpoint = NewAssignment(Provider.Anthropic);

        var oldCredential = await provider.GetCredentialAsync(endpoint);
        provider.Invalidate("endpoints/abc");
        var newCredential = await provider.GetCredentialAsync(endpoint);

        oldCredential.HeaderValue.Should().Be("sk-old");
        newCredential.HeaderValue.Should().Be("sk-new");
        await secretResolver.Received(2).TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCredentialAsync_WhenSecretMissingInVault_Throws()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>()).Returns((string?)null);
        var provider = new ApiKeyCredentialProvider(secretResolver, new MemoryCache(new MemoryCacheOptions()));

        var act = () => provider.GetCredentialAsync(NewAssignment(Provider.OpenAI));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
