using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Domain.Entities;

namespace AiProxy.Application.Consumers;

public sealed record OidcConsumerIdentity(string Issuer, string Subject, string Username);

public interface IOidcConsumerResolver
{
    Task<Consumer> ResolveAsync(
        OidcConsumerIdentity identity,
        CancellationToken cancellationToken = default);
}

public sealed class OidcConsumerResolver(IConsumerRepository repository) : IOidcConsumerResolver
{
    public async Task<Consumer> ResolveAsync(
        OidcConsumerIdentity identity,
        CancellationToken cancellationToken = default)
    {
        var candidate = Consumer.CreateOidc(
            Guid.NewGuid(),
            identity.Username,
            identity.Issuer,
            identity.Subject);

        var existing = await repository.GetByOidcIdentityAsync(
            candidate.OidcIssuer!,
            candidate.OidcSubject!,
            cancellationToken);

        if (existing is not null)
        {
            return await RefreshNameAsync(existing, identity.Username, cancellationToken);
        }

        if (await repository.ExistsByNameAsync(identity.Username, cancellationToken))
        {
            throw UsernameConflict(identity.Username);
        }

        if (await repository.TryAddAsync(candidate, cancellationToken))
        {
            return candidate;
        }

        existing = await repository.GetByOidcIdentityAsync(
            candidate.OidcIssuer!,
            candidate.OidcSubject!,
            cancellationToken);

        if (existing is null)
        {
            throw UsernameConflict(identity.Username);
        }

        return await RefreshNameAsync(existing, identity.Username, cancellationToken);
    }

    private async Task<Consumer> RefreshNameAsync(
        Consumer consumer,
        string username,
        CancellationToken cancellationToken)
    {
        if (consumer.Name == username)
        {
            return consumer;
        }

        if (await repository.ExistsByNameAsync(username, cancellationToken))
        {
            throw UsernameConflict(username);
        }

        consumer.RefreshName(username);
        await repository.UpdateAsync(consumer, cancellationToken);
        return consumer;
    }

    private static ConflictException UsernameConflict(string username) =>
        new($"The OIDC username '{username}' is already assigned to another consumer.");
}