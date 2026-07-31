using AiProxy.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AiProxy.Infrastructure.Security;

/// <summary>
/// Resolves the caller's <c>X-Api-Key</c> header (or, as a fallback, an <c>api_key</c> query parameter) to a
/// <see cref="Domain.Entities.Consumer"/> via <see cref="IConsumerAuthenticator"/> (architecture.md §13)
/// before an ingress inference route runs, and stashes the resolved identity in
/// <see cref="HttpContext.Items"/> for routing/usage capture to read. Missing, unknown, or revoked keys are
/// rejected with 401 before any routing/forwarding work happens.
/// </summary>
public sealed class ConsumerAuthEndpointFilter : IEndpointFilter
{
    public const string ConsumerIdItemKey = "AiProxy.ConsumerId";
    public const string ConsumerNameItemKey = "AiProxy.ConsumerName";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        var apiKey = ResolveApiKey(httpContext);
        if (apiKey is null)
        {
            return Results.Json(
                new { error = "Missing X-Api-Key header (or 'api_key' query parameter for WebSocket routes)." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Resolved per-request (not constructor-injected): this filter instance is built once from the app's
        // root provider when routes are constructed, so a scoped dependency like IConsumerAuthenticator must
        // come from the current request's DI scope instead.
        var authenticator = httpContext.RequestServices.GetRequiredService<IConsumerAuthenticator>();
        var consumer = await authenticator.AuthenticateAsync(apiKey, httpContext.RequestAborted);
        if (consumer is null)
        {
            return Results.Json(new { error = "Invalid or revoked API key." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        httpContext.Items[ConsumerIdItemKey] = consumer.Id;
        httpContext.Items[ConsumerNameItemKey] = consumer.Name;

        return await next(context);
    }

    /// <summary>
    /// Header takes precedence; the query parameter exists only because a browser's native <c>WebSocket</c>
    /// API cannot set custom headers, so the frontend's Realtime Playground (architecture.md §6.5) has no
    /// other way to authenticate. Query strings can end up in server/proxy access logs, so this is
    /// deliberately a fallback, not the primary mechanism, for every other route.
    /// </summary>
    private static string? ResolveApiKey(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue("X-Api-Key", out var header) && header.Count > 0 && !string.IsNullOrEmpty(header[0]))
        {
            return header[0];
        }

        if (httpContext.Request.Query.TryGetValue("api_key", out var query) && query.Count > 0 && !string.IsNullOrEmpty(query[0]))
        {
            return query[0];
        }

        return null;
    }
}
