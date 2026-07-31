using AiProxy.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AiProxy.Infrastructure.Security;

/// <summary>
/// Resolves the caller's <c>X-Api-Key</c> header to a <see cref="Domain.Entities.Consumer"/> via
/// <see cref="IConsumerAuthenticator"/> (architecture.md §13) before an ingress inference route runs, and
/// stashes the resolved identity in <see cref="HttpContext.Items"/> for routing/usage capture to read.
/// Missing, unknown, or revoked keys are rejected with 401 before any routing/forwarding work happens.
/// </summary>
public sealed class ConsumerAuthEndpointFilter : IEndpointFilter
{
    public const string ConsumerIdItemKey = "AiProxy.ConsumerId";
    public const string ConsumerNameItemKey = "AiProxy.ConsumerName";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (!httpContext.Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader) || apiKeyHeader.Count == 0)
        {
            return Results.Json(new { error = "Missing X-Api-Key header." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        // Resolved per-request (not constructor-injected): this filter instance is built once from the app's
        // root provider when routes are constructed, so a scoped dependency like IConsumerAuthenticator must
        // come from the current request's DI scope instead.
        var authenticator = httpContext.RequestServices.GetRequiredService<IConsumerAuthenticator>();
        var consumer = await authenticator.AuthenticateAsync(apiKeyHeader[0]!, httpContext.RequestAborted);
        if (consumer is null)
        {
            return Results.Json(new { error = "Invalid or revoked API key." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        httpContext.Items[ConsumerIdItemKey] = consumer.Id;
        httpContext.Items[ConsumerNameItemKey] = consumer.Name;

        return await next(context);
    }
}
