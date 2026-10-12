using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Consumers;
using AIProxyPlus.Domain.Entities;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace AIProxyPlus.Application.Tests.Consumers;

public class CreateFederatedConsumerTests
{
    private readonly IConsumerRepository _repository = Substitute.For<IConsumerRepository>();

    [Fact]
    public async Task CreateOidcConsumer_NormalizesIssuerAndPersistsKeylessConsumer()
    {
        var handler = new CreateOidcConsumerHandler(_repository, new CreateOidcConsumerValidator());

        var consumer = await handler.HandleAsync(new CreateOidcConsumerCommand(
            "release-agent", "https://identity.example.com/realms/workloads/", "agent-42"));

        consumer.OidcIssuer.Should().Be("https://identity.example.com/realms/workloads");
        consumer.OidcSubject.Should().Be("agent-42");
        consumer.ApiKeyHash.Should().BeNull();
        await _repository.Received(1).AddAsync(consumer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateManagedIdentityConsumer_PersistsImmutableDirectoryIdentity()
    {
        var handler = new CreateManagedIdentityConsumerHandler(
            _repository, new CreateManagedIdentityConsumerValidator());
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();

        var consumer = await handler.HandleAsync(
            new CreateManagedIdentityConsumerCommand("payments-worker", tenantId, objectId));

        consumer.ManagedIdentityTenantId.Should().Be(tenantId);
        consumer.ManagedIdentityObjectId.Should().Be(objectId);
        await _repository.Received(1).AddAsync(consumer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateManagedIdentityConsumer_WithEmptyObjectId_IsRejected()
    {
        var handler = new CreateManagedIdentityConsumerHandler(
            _repository, new CreateManagedIdentityConsumerValidator());

        var action = () => handler.HandleAsync(
            new CreateManagedIdentityConsumerCommand("payments-worker", Guid.NewGuid(), Guid.Empty));

        await action.Should().ThrowAsync<ValidationException>();
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}