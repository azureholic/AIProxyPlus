using AiProxy.Domain.Entities;

namespace AiProxy.Infrastructure.Persistence;

public sealed class RealtimeTicket
{
    public string TicketHash { get; private set; } = null!;
    public Guid ConsumerId { get; private set; }
    public Consumer Consumer { get; private set; } = null!;
    public string Route { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    private RealtimeTicket()
    {
    }

    public static RealtimeTicket Create(
        string ticketHash,
        Consumer consumer,
        string route,
        DateTimeOffset expiresAt) =>
        new()
        {
            TicketHash = ticketHash,
            ConsumerId = consumer.Id,
            Consumer = consumer,
            Route = route,
            ExpiresAt = expiresAt,
        };
}