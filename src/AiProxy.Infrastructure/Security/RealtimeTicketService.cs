using System.Security.Cryptography;
using System.Text;
using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace AiProxy.Infrastructure.Security;

public sealed class RealtimeTicketService(AiProxyDbContext dbContext) : IRealtimeTicketService
{
    private const int TicketByteLength = 32;

    public async Task<string> IssueAsync(
        Consumer consumer,
        string route,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        if (!consumer.IsOidc || !consumer.Enabled)
        {
            throw new InvalidOperationException("Realtime tickets can only be issued to enabled OIDC consumers.");
        }
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        var plaintextTicket = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TicketByteLength));
        dbContext.Set<RealtimeTicket>().Add(RealtimeTicket.Create(
            Hash(plaintextTicket),
            consumer,
            route,
            DateTimeOffset.UtcNow.Add(lifetime)));
        await dbContext.SaveChangesAsync(cancellationToken);
        return plaintextTicket;
    }

    public async Task<Consumer?> ConsumeAsync(
        string plaintextTicket,
        string route,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidFormat(plaintextTicket) || string.IsNullOrWhiteSpace(route))
        {
            return null;
        }

        var ticketHash = Hash(plaintextTicket);
        var now = DateTimeOffset.UtcNow;
        var consumed = await dbContext.Set<RealtimeTicket>()
            .Where(ticket =>
                ticket.TicketHash == ticketHash &&
                ticket.Route == route &&
                ticket.ConsumedAt == null &&
                ticket.ExpiresAt > now &&
                ticket.Consumer.Enabled &&
                ticket.Consumer.OidcIssuer != null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(ticket => ticket.ConsumedAt, now),
                cancellationToken);

        if (consumed != 1)
        {
            return null;
        }

        return await dbContext.Set<RealtimeTicket>()
            .Where(ticket => ticket.TicketHash == ticketHash)
            .Select(ticket => ticket.Consumer)
            .SingleAsync(cancellationToken);
    }

    private static bool IsValidFormat(string ticket)
    {
        try
        {
            return WebEncoders.Base64UrlDecode(ticket).Length == TicketByteLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Hash(string plaintextTicket) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintextTicket)));
}