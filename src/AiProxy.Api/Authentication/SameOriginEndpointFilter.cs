namespace AiProxy.Api.Authentication;

public sealed class SameOriginEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.Headers.TryGetValue("Sec-Fetch-Site", out var fetchSite) &&
            fetchSite.Count > 0 &&
            fetchSite[0] is not ("same-origin" or "none"))
        {
            return Results.BadRequest(new { error = "Cross-origin authentication requests are not allowed." });
        }

        if (request.Headers.TryGetValue("Origin", out var origins) && origins.Count > 0 &&
            (!Uri.TryCreate(origins[0], UriKind.Absolute, out var origin) ||
             !string.Equals(origin.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(origin.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.BadRequest(new { error = "Cross-origin authentication requests are not allowed." });
        }

        return await next(context);
    }
}