using AiProxy.Application.Abstractions;
using AiProxy.Application.Consumers;
using Microsoft.AspNetCore.Authentication;
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
    public const string AllowedModelIdsItemKey = "AiProxy.AllowedModelIds";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        var headerApiKey = ResolveHeaderApiKey(httpContext);
        if (headerApiKey is not null)
        {
            return await AuthenticateApiKeyAsync(httpContext, headerApiKey, context, next);
        }

        if (httpContext.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return await AuthenticateBearerAsync(httpContext, context, next);
        }

        if (httpContext.Request.Path.Equals(RealtimeTicketRoutes.OpenAiRealtime))
        {
            if (httpContext.Request.Query.TryGetValue("ticket", out var ticketValues) &&
                ticketValues.Count > 0 &&
                !string.IsNullOrWhiteSpace(ticketValues[0]))
            {
                var ticketService = httpContext.RequestServices.GetRequiredService<IRealtimeTicketService>();
                var ticketConsumer = await ticketService.ConsumeAsync(
                    ticketValues[0]!,
                    RealtimeTicketRoutes.OpenAiRealtime,
                    httpContext.RequestAborted);
                if (ticketConsumer is null)
                {
                    return Results.Json(new { error = "Invalid, expired, or already consumed Realtime ticket." },
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                SetConsumer(httpContext, ticketConsumer);
                return await next(context);
            }

            var queryApiKey = ResolveQueryApiKey(httpContext);
            if (queryApiKey is not null)
            {
                return await AuthenticateApiKeyAsync(httpContext, queryApiKey, context, next);
            }

            return Results.Json(
                new { error = "Missing X-Api-Key header, Realtime ticket, or 'api_key' query parameter." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var issuer = httpContext.User.FindFirst("iss")?.Value;
            var subject = httpContext.User.FindFirst("sub")?.Value;
            var username = httpContext.User.Identity.Name;

            if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(username))
            {
                return Results.Json(new { error = "The authenticated session is missing required identity claims." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var resolver = httpContext.RequestServices.GetRequiredService<IOidcConsumerResolver>();
            var sessionConsumer = await resolver.ResolveAsync(
                new OidcConsumerIdentity(issuer, subject, username),
                httpContext.RequestAborted);

            if (!sessionConsumer.Enabled)
            {
                return Results.Json(new { error = "This consumer is disabled." }, statusCode: StatusCodes.Status403Forbidden);
            }

            SetConsumer(httpContext, sessionConsumer);
            return await next(context);
        }

        return Results.Json(new { error = "Missing X-Api-Key header." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async ValueTask<object?> AuthenticateBearerAsync(
        HttpContext httpContext,
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var options = httpContext.RequestServices.GetRequiredService<WorkloadAuthenticationOptions>();
        var resolver = httpContext.RequestServices.GetRequiredService<IFederatedConsumerResolver>();

        if (options.ManagedIdentity.Enabled)
        {
            var result = await httpContext.AuthenticateAsync(
                WorkloadAuthenticationOptions.ManagedIdentityScheme);
            if (result.Succeeded && result.Principal is not null)
            {
                if (!result.Principal.FindAll("roles").Any(claim =>
                        string.Equals(claim.Value, options.ManagedIdentity.RequiredRole, StringComparison.Ordinal)))
                {
                    return Results.Json(new { error = "The bearer token lacks the required application role." },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                if (!Guid.TryParse(result.Principal.FindFirst("tid")?.Value, out var tenantId) ||
                    !Guid.TryParse(result.Principal.FindFirst("oid")?.Value, out var objectId))
                {
                    return Results.Json(new { error = "The managed identity token lacks valid tid or oid claims." },
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                var consumer = await resolver.ResolveManagedIdentityAsync(
                    new ManagedIdentityBearerIdentity(tenantId, objectId), httpContext.RequestAborted);
                return await ContinueAsFederatedConsumerAsync(httpContext, consumer, context, next);
            }
        }

        if (options.Oidc.Enabled)
        {
            var result = await httpContext.AuthenticateAsync(WorkloadAuthenticationOptions.OidcScheme);
            if (result.Succeeded && result.Principal is not null)
            {
                var scopes = result.Principal.FindFirst("scope")?.Value ?? result.Principal.FindFirst("scp")?.Value;
                if (string.IsNullOrWhiteSpace(scopes) || !scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Contains(options.Oidc.RequiredScope, StringComparer.Ordinal))
                {
                    return Results.Json(new { error = "The bearer token lacks the required scope." },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                var issuer = result.Principal.FindFirst("iss")?.Value;
                var subject = result.Principal.FindFirst("sub")?.Value;
                if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
                {
                    return Results.Json(new { error = "The bearer token lacks required iss or sub claims." },
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                var consumer = await resolver.ResolveOidcAsync(
                    new OidcBearerIdentity(issuer, subject), httpContext.RequestAborted);
                return await ContinueAsFederatedConsumerAsync(httpContext, consumer, context, next);
            }
        }

        return Results.Json(new { error = "Invalid bearer token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async ValueTask<object?> ContinueAsFederatedConsumerAsync(
        HttpContext httpContext,
        Domain.Entities.Consumer? consumer,
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (consumer is null)
        {
            return Results.Json(new { error = "This bearer identity is not enrolled." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!consumer.Enabled)
        {
            return Results.Json(new { error = "This consumer is disabled." }, statusCode: StatusCodes.Status403Forbidden);
        }

        SetConsumer(httpContext, consumer);
        return await next(context);
    }

    private static async ValueTask<object?> AuthenticateApiKeyAsync(
        HttpContext httpContext,
        string apiKey,
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var authenticator = httpContext.RequestServices.GetRequiredService<IConsumerAuthenticator>();
        var consumer = await authenticator.AuthenticateAsync(apiKey, httpContext.RequestAborted);
        if (consumer is null)
        {
            return Results.Json(new { error = "Invalid or revoked API key." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        SetConsumer(httpContext, consumer);

        return await next(context);
    }

    private static void SetConsumer(HttpContext httpContext, Domain.Entities.Consumer consumer)
    {
        httpContext.Items[ConsumerIdItemKey] = consumer.Id;
        httpContext.Items[ConsumerNameItemKey] = consumer.Name;
        httpContext.Items[AllowedModelIdsItemKey] = consumer.HasUnrestrictedModelAccess
            ? null
            : consumer.AllowedModelIds.ToHashSet();
    }

    /// <summary>
    /// Header takes precedence; the query parameter exists only because a browser's native <c>WebSocket</c>
    /// API cannot set custom headers, so the frontend's Realtime Playground (architecture.md §6.5) has no
    /// other way to authenticate. Query strings can end up in server/proxy access logs, so this is
    /// deliberately a fallback, not the primary mechanism, for every other route.
    /// </summary>
    private static string? ResolveHeaderApiKey(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue("X-Api-Key", out var header) && header.Count > 0 && !string.IsNullOrEmpty(header[0]))
        {
            return header[0];
        }

        return null;
    }

    private static string? ResolveQueryApiKey(HttpContext httpContext)
    {
        if (httpContext.Request.Query.TryGetValue("api_key", out var query) && query.Count > 0 && !string.IsNullOrEmpty(query[0]))
        {
            return query[0];
        }

        return null;
    }
}
