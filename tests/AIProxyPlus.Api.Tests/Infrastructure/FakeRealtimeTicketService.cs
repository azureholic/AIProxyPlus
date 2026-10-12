using System.Collections.Concurrent;
using System.Security.Cryptography;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Entities;
using Microsoft.AspNetCore.WebUtilities;

namespace AIProxyPlus.Api.Tests.Infrastructure;

public sealed class FakeRealtimeTicketService : IRealtimeTicketService
{
    private readonly ConcurrentDictionary<string, TicketState> _tickets = new();

    public Task<string> IssueAsync(
        Consumer consumer,
        string route,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        if (!consumer.IsOidc || !consumer.Enabled)
        {
            throw new InvalidOperationException("Realtime tickets can only be issued to enabled OIDC consumers.");
        }

        var ticket = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _tickets[ticket] = new TicketState(consumer, route, DateTimeOffset.UtcNow.Add(lifetime));
        return Task.FromResult(ticket);
    }

    public Task<Consumer?> ConsumeAsync(
        string plaintextTicket,
        string route,
        CancellationToken cancellationToken = default)
    {
        if (!_tickets.TryRemove(plaintextTicket, out var state) ||
            state.Route != route ||
            state.ExpiresAt <= DateTimeOffset.UtcNow ||
            !state.Consumer.Enabled ||
            !state.Consumer.IsOidc)
        {
            return Task.FromResult<Consumer?>(null);
        }

        return Task.FromResult<Consumer?>(state.Consumer);
    }

    private sealed record TicketState(Consumer Consumer, string Route, DateTimeOffset ExpiresAt);
}