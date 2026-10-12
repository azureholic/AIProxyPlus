using AIProxyPlus.Infrastructure.Secrets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AIProxyPlus.Infrastructure.Security;

/// <summary>
/// Requires a valid <c>X-Admin-Api-Key</c> header (architecture.md §7.2/§13) on the configuration/usage-query
/// API. Applied to the whole <c>/api/v1</c> surface, not just write routes — model topology and
/// per-consumer usage figures are both sensitive enough to keep behind admin auth even for reads.
/// </summary>
public sealed class AdminAuthEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        httpContext.Request.Headers.TryGetValue("X-Admin-Api-Key", out var header);

        var authenticator = httpContext.RequestServices.GetRequiredService<AdminApiKeyAuthenticator>();
        if (!authenticator.Validate(header.Count > 0 ? header[0] : null))
        {
            return Results.Json(new { error = "Missing or invalid X-Admin-Api-Key header." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        return await next(context);
    }
}
