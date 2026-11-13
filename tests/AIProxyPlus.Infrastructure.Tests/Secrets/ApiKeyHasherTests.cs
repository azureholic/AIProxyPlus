using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Infrastructure.Secrets;
using FluentAssertions;
using NSubstitute;

namespace AIProxyPlus.Infrastructure.Tests.Secrets;

public class ApiKeyHasherTests
{
    [Fact]
    public void Hash_IsDeterministic_ForSameKeyAndPepper()
    {
        var hasher = new ApiKeyHasher("pepper-1");

        hasher.Hash("my-api-key").Should().Be(hasher.Hash("my-api-key"));
    }

    [Fact]
    public void Hash_Differs_ForDifferentPeppers()
    {
        var hasherA = new ApiKeyHasher("pepper-a");
        var hasherB = new ApiKeyHasher("pepper-b");

        hasherA.Hash("my-api-key").Should().NotBe(hasherB.Hash("my-api-key"));
    }

    [Fact]
    public void Hash_Differs_ForDifferentKeys()
    {
        var hasher = new ApiKeyHasher("pepper-1");

        hasher.Hash("key-a").Should().NotBe(hasher.Hash("key-b"));
    }

    [Fact]
    public void Verify_WithCorrectKey_ReturnsTrue()
    {
        var hasher = new ApiKeyHasher("pepper-1");
        var hash = hasher.Hash("my-api-key");

        hasher.Verify("my-api-key", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WithWrongKey_ReturnsFalse()
    {
        var hasher = new ApiKeyHasher("pepper-1");
        var hash = hasher.Hash("my-api-key");

        hasher.Verify("wrong-key", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyPepper_Throws(string pepper)
    {
        var act = () => new ApiKeyHasher(pepper);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_WhenNoPepperExists_ProvisionsAndStoresANewOne()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("consumer-key-pepper", Arg.Any<CancellationToken>()).Returns((string?)null);

        var hasher = await ApiKeyHasher.CreateAsync(secretResolver);

        await secretResolver.Received(1).StoreAsync("consumer-key-pepper", Arg.Any<string>(), Arg.Any<CancellationToken>());
        hasher.Hash("x").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateAsync_ProvisioningDisabledAndPepperMissing_FailsWithoutWriting()
    {
        var secrets = Substitute.For<ISecretResolver>();
        secrets.TryResolveAsync("consumer-key-pepper", Arg.Any<CancellationToken>()).Returns((string?)null);

        var act = () => ApiKeyHasher.CreateAsync(secrets, autoProvision: false);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*before starting the API*");
        await secrets.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default);
    }

    [Fact]
    public async Task CreateAsync_ProvisioningDisabled_ReplicasAndRestartReuseExistingPepper()
    {
        var secrets = Substitute.For<ISecretResolver>();
        secrets.TryResolveAsync("consumer-key-pepper", Arg.Any<CancellationToken>()).Returns("existing-pepper");
        var first = await ApiKeyHasher.CreateAsync(secrets, autoProvision: false);
        var replica = await ApiKeyHasher.CreateAsync(secrets, autoProvision: false);
        var replacement = await ApiKeyHasher.CreateAsync(secrets, autoProvision: false);

        replica.Verify("key", first.Hash("key")).Should().BeTrue();
        replacement.Verify("key", first.Hash("key")).Should().BeTrue();
        await secrets.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default);
    }

    [Fact]
    public async Task CreateAsync_WhenPepperAlreadyExists_ReusesIt_AndDoesNotStoreAgain()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.TryResolveAsync("consumer-key-pepper", Arg.Any<CancellationToken>()).Returns("existing-pepper");

        var hasher = await ApiKeyHasher.CreateAsync(secretResolver);

        await secretResolver.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default);
        hasher.Hash("x").Should().Be(new ApiKeyHasher("existing-pepper").Hash("x"));
    }
}
