using AiProxy.Application.Abstractions;
using AiProxy.Application.Consumers;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Consumers;

public class OidcConsumerResolverTests
{
    private readonly IConsumerRepository _repository = Substitute.For<IConsumerRepository>();
    private readonly OidcConsumerResolver _resolver;

    public OidcConsumerResolverTests()
    {
        _resolver = new OidcConsumerResolver(_repository);
    }

    [Fact]
    public async Task ResolveAsync_WithNewIdentity_ProvisionsOidcConsumer()
    {
        _repository.TryAddAsync(Arg.Any<Consumer>(), Arg.Any<CancellationToken>()).Returns(true);

        var consumer = await _resolver.ResolveAsync(new OidcConsumerIdentity(
            "https://identity.example.com/realms/ai-proxy/",
            "subject-1",
            "proxy-admin"));

        consumer.Name.Should().Be("proxy-admin");
        consumer.OidcIssuer.Should().Be("https://identity.example.com/realms/ai-proxy");
        consumer.OidcSubject.Should().Be("subject-1");
        consumer.ApiKeyHash.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_WithExistingIdentity_RefreshesUsername()
    {
        var consumer = Consumer.CreateOidc(
            Guid.NewGuid(),
            "old-name",
            "https://identity.example.com/realms/ai-proxy",
            "subject-1");
        _repository.GetByOidcIdentityAsync(
            consumer.OidcIssuer!,
            consumer.OidcSubject!,
            Arg.Any<CancellationToken>()).Returns(consumer);

        var resolved = await _resolver.ResolveAsync(new OidcConsumerIdentity(
            consumer.OidcIssuer!,
            consumer.OidcSubject!,
            "new-name"));

        resolved.Name.Should().Be("new-name");
        await _repository.Received(1).UpdateAsync(consumer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_WithConflictingUsername_ThrowsConflict()
    {
        _repository.ExistsByNameAsync("proxy-admin", Arg.Any<CancellationToken>()).Returns(true);

        var act = () => _resolver.ResolveAsync(new OidcConsumerIdentity(
            "https://identity.example.com/realms/ai-proxy",
            "subject-1",
            "proxy-admin"));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*proxy-admin*");
        await _repository.DidNotReceiveWithAnyArgs().TryAddAsync(default!, default);
    }
}