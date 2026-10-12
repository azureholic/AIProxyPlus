using System.Net.Http;
using System.Net.Sockets;
using AIProxyPlus.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace AIProxyPlus.Infrastructure.Routing;

/// <summary>
/// Decides whether a failed upstream attempt may be replayed. Every proxied operation is a non-idempotent POST, so
/// a replay is only allowed when the failure shows the upstream did not process the request (architecture.md §9,
/// "Replay safety").
/// </summary>
public static class UpstreamFailureClassifier
{
    private const int AnthropicOverloaded = 529;

    /// <summary>
    /// A response status that says the upstream rejected the request before processing it: 429 (rate limited) and
    /// 503 (temporarily unavailable) for every provider, plus 529, which only Anthropic documents as an
    /// "overloaded" rejection. 529 is not a standard status, so another provider could use it after accepting work
    /// and it is not trusted there.
    /// </summary>
    public static bool IsSafeToReplay(Provider provider, int statusCode) =>
        statusCode is StatusCodes.Status429TooManyRequests or StatusCodes.Status503ServiceUnavailable
        || (provider == Provider.Anthropic && statusCode == AnthropicOverloaded);

    /// <summary>
    /// A transport failure that proves the request never reached the upstream. HttpRequestError.ConnectionError alone
    /// is not enough: .NET also reports a connection that was reset after the request was sent as ConnectionError,
    /// so it counts only when the socket error says no connection was made. A connect timeout surfaces as a
    /// TaskCanceledException wrapping a TimeoutException. The per-attempt response timeout (Endpoint.TimeoutSeconds)
    /// is enforced by the caller with its own token, so it is never a TimeoutException cause and must be treated as
    /// ambiguous: the request was sent and may have been processed.
    /// </summary>
    public static bool FailedBeforeRequestWasSent(Exception exception) => exception switch
    {
        TaskCanceledException { InnerException: TimeoutException } => true,
        HttpRequestException http => http.HttpRequestError is HttpRequestError.NameResolutionError
                or HttpRequestError.SecureConnectionError
                or HttpRequestError.ProxyTunnelError
            || (http.HttpRequestError == HttpRequestError.ConnectionError
                && http.InnerException is SocketException
                {
                    SocketErrorCode: SocketError.ConnectionRefused
                        or SocketError.HostUnreachable
                        or SocketError.NetworkUnreachable
                        or SocketError.AddressNotAvailable
                        or SocketError.HostNotFound
                }),
        _ => false,
    };
}
