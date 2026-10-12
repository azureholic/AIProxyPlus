using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace AIProxyPlus.Infrastructure.Tests.Secrets;

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
    public async Task Invalidate_DuringSecretResolution_DoesNotRepopulateRetiredReference()
    {
        var firstResolutionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeFirstResolution = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>())
            .Returns(
                _ =>
                {
                    firstResolutionStarted.SetResult();
                    return completeFirstResolution.Task;
                },
                _ => Task.FromResult<string?>("sk-new"));
        var provider = new ApiKeyCredentialProvider(
            secretResolver, new MemoryCache(new MemoryCacheOptions()), TimeSpan.FromMinutes(5));
        var endpoint = NewAssignment(Provider.OpenAI);

        var inFlightCredential = provider.GetCredentialAsync(endpoint);
        await firstResolutionStarted.Task;
        provider.Invalidate("endpoints/abc");
        completeFirstResolution.SetResult("sk-old");

        (await inFlightCredential).HeaderValue.Should().Be("Bearer sk-old");
        (await provider.GetCredentialAsync(endpoint)).HeaderValue.Should().Be("Bearer sk-new");
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

    [Fact]
    public async Task GetCredentialCandidatesAsync_WithConfiguredFallback_ResolvesOnlyActiveCredential()
    {
        var assignment = NewAssignment(Provider.OpenAI);
        assignment.Endpoint.SetSecretReference(EndpointCredentialSlot.Secondary, "endpoints/secondary");
        assignment.Endpoint.ActivateCredential(
            EndpointCredentialSlot.Secondary,
            DateTimeOffset.UtcNow.AddMinutes(5),
            DateTimeOffset.UtcNow);
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("endpoints/secondary", Arg.Any<CancellationToken>()).Returns("sk-active");
        secretResolver.TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ => throw new InvalidOperationException("Fallback unavailable"));
        var provider = new ApiKeyCredentialProvider(secretResolver, new MemoryCache(new MemoryCacheOptions()));

        var candidates = await provider.GetCredentialCandidatesAsync(assignment);

        candidates.Active.HeaderValue.Should().Be("Bearer sk-active");
        candidates.ResolveFallbackAsync.Should().NotBeNull();
        await secretResolver.DidNotReceive().TryResolveAsync("endpoints/abc", Arg.Any<CancellationToken>());
    }
}
