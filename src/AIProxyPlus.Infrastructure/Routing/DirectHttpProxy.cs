using System.Net.Http.Headers;
using System.Text;
using AIProxyPlus.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace AIProxyPlus.Infrastructure.Routing;

/// <summary>
/// Builds outbound requests to a selected <see cref="Endpoint"/> and copies the winning response back to the
/// client. Deliberately NOT built on YARP's <c>IHttpForwarder</c>: that API commits headers/status to the
/// client response as soon as it receives them from upstream, which makes it impossible to inspect a response
/// (to decide "retry on this backend's 429/503" or to return its other 5xx as final) before it has already been
/// forwarded. Since our bounded
/// per-endpoint retry with failover (architecture.md §9) requires exactly that inspect-before-commit
/// capability, requests are issued directly via <see cref="HttpMessageInvoker"/> and only the response for
/// the attempt that is ultimately accepted is copied onto <see cref="HttpContext.Response"/>.
/// </summary>
internal static class DirectHttpProxy
{
    private static readonly HashSet<string> HopByHopRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Transfer-Encoding", "TE", "Trailer", "Upgrade", "Proxy-Authorization",
        "Proxy-Connection", "Host", "Content-Length", "Content-Type",
    };

    private static readonly HashSet<string> ProxyLocalRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Origin", "Referer", "X-Api-Key", "X-Admin-Api-Key", "X-CSRF-TOKEN", "Forwarded",
    };

    private static readonly HashSet<string> HopByHopResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Transfer-Encoding", "TE", "Trailer", "Upgrade", "Proxy-Authenticate", "Content-Length",
    };

    public static HttpRequestMessage BuildRequest(HttpContext httpContext, Uri targetUri, byte[] bodyBytes, EndpointCredential credential)
    {
        var request = new HttpRequestMessage(new HttpMethod(httpContext.Request.Method), targetUri);

        if (bodyBytes.Length > 0)
        {
            request.Content = new ByteArrayContent(bodyBytes);
            if (MediaTypeHeaderValue.TryParse(httpContext.Request.ContentType, out var contentType))
            {
                request.Content.Headers.ContentType = contentType;
            }
        }

        foreach (var header in httpContext.Request.Headers)
        {
            if (ShouldExcludeRequestHeader(header.Key, credential.HeaderName))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string?>)header.Value!);
        }

        request.Headers.Remove(credential.HeaderName);
        request.Headers.TryAddWithoutValidation(credential.HeaderName, credential.HeaderValue);

        // Buffered JSON and SSE processing require uncompressed upstream response bytes.
        request.Headers.Remove("Accept-Encoding");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        return request;
    }

    private static bool ShouldExcludeRequestHeader(string headerName, string credentialHeaderName) =>
        HopByHopRequestHeaders.Contains(headerName) ||
        ProxyLocalRequestHeaders.Contains(headerName) ||
        string.Equals(headerName, credentialHeaderName, StringComparison.OrdinalIgnoreCase) ||
        headerName.StartsWith("Sec-Fetch-", StringComparison.OrdinalIgnoreCase) ||
        headerName.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase);

    /// <summary>Streams a non-buffered upstream response straight through to the client.</summary>
    public static async Task CopyResponseAsync(HttpResponseMessage response, HttpContext httpContext, CancellationToken cancellationToken)
    {
        CopyStatusAndHeaders(response, httpContext);
        await response.Content.CopyToAsync(httpContext.Response.Body, cancellationToken);
    }

    /// <summary>Writes an already-buffered (peeked) upstream response body to the client.</summary>
    public static async Task CopyBufferedResponseAsync(
        HttpResponseMessage response, string bufferedBody, HttpContext httpContext, CancellationToken cancellationToken)
    {
        CopyStatusAndHeaders(response, httpContext);
        var bytes = Encoding.UTF8.GetBytes(bufferedBody);
        httpContext.Response.ContentLength = bytes.Length;
        await httpContext.Response.Body.WriteAsync(bytes, cancellationToken);
    }

    internal static void CopyStatusAndHeaders(HttpResponseMessage response, HttpContext httpContext)
    {
        httpContext.Response.StatusCode = (int)response.StatusCode;

        foreach (var header in response.Headers)
        {
            if (!HopByHopResponseHeaders.Contains(header.Key))
            {
                httpContext.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in response.Content.Headers)
        {
            if (!HopByHopResponseHeaders.Contains(header.Key))
            {
                httpContext.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }
    }

    /// <summary>
    /// Buffers a failed upstream response (status, headers incl. any <c>Retry-After</c>/rate-limit headers,
    /// body) so it can be replayed to the client later if every endpoint ends up failing (architecture.md §9).
    /// </summary>
    internal static async Task<UpstreamFailureSnapshot> CaptureFailureSnapshotAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            if (!HopByHopResponseHeaders.Contains(header.Key))
            {
                headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in response.Content.Headers)
        {
            if (!HopByHopResponseHeaders.Contains(header.Key))
            {
                headers[header.Key] = header.Value.ToArray();
            }
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new UpstreamFailureSnapshot((int)response.StatusCode, headers, body);
    }

    internal static async Task ReplaySnapshotAsync(
        UpstreamFailureSnapshot snapshot,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = snapshot.StatusCode;
        foreach (var header in snapshot.Headers)
        {
            httpContext.Response.Headers[header.Key] = header.Value;
        }
        var bytes = Encoding.UTF8.GetBytes(snapshot.Body);
        httpContext.Response.ContentLength = bytes.Length;
        await httpContext.Response.Body.WriteAsync(bytes, cancellationToken);
    }
}
