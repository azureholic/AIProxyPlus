using AIProxyPlus.Domain.Entities;

namespace AIProxyPlus.Application.Abstractions;

public static class RealtimeTicketRoutes
{
    public const string OpenAiRealtime = "/openai/v1/realtime";
}

public interface IRealtimeTicketService
{
    Task<string> IssueAsync(
        Consumer consumer,
        string route,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task<Consumer?> ConsumeAsync(
        string plaintextTicket,
        string route,
        CancellationToken cancellationToken = default);
}