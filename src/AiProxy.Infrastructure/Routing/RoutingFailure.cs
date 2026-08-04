using System.Text;
using Microsoft.AspNetCore.Http;

namespace AiProxy.Infrastructure.Routing;

public enum RoutingFailureReason
{
    ModelNotFound,
    ModelAccessDenied,
    NoHealthyEndpoint,
    StickyEndpointUnavailable,
}

/// <summary>
/// A routing decision that failed before any request was forwarded to a backend, or (when
/// <see cref="UpstreamFailure"/> is set) after every candidate endpoint was tried and failed.
/// </summary>
public sealed record RoutingFailure(RoutingFailureReason Reason, string Message, UpstreamFailureSnapshot? UpstreamFailure = null);

/// <summary>
/// A buffered copy of the first endpoint's failed response for a request (status, headers, body) —
/// preserved so it can be relayed back to the client verbatim once all endpoints are exhausted, instead of
/// being replaced by a generic 503 (architecture.md §9). Carrying the headers matters in particular for a
/// `429`: the client needs the original <c>Retry-After</c>/rate-limit headers to know how long to back off.
/// </summary>
public sealed record UpstreamFailureSnapshot(int StatusCode, IReadOnlyDictionary<string, string[]> Headers, string Body)
{
    public async Task WriteToAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCode;
        foreach (var header in Headers)
        {
            httpContext.Response.Headers[header.Key] = header.Value;
        }

        var bytes = Encoding.UTF8.GetBytes(Body);
        httpContext.Response.ContentLength = bytes.Length;
        await httpContext.Response.Body.WriteAsync(bytes, cancellationToken);
    }
}

