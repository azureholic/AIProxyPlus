using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Infrastructure.Routing;
using FluentAssertions;

namespace AIProxyPlus.Infrastructure.Tests.Routing;

public class UpstreamFailureClassifierTests
{
    [Theory]
    [InlineData(Provider.OpenAI, 429, true)]
    [InlineData(Provider.MicrosoftAI, 429, true)]
    [InlineData(Provider.Anthropic, 429, true)]
    [InlineData(Provider.OpenAI, 503, true)]
    [InlineData(Provider.MicrosoftAI, 503, true)]
    [InlineData(Provider.Anthropic, 503, true)]
    [InlineData(Provider.Anthropic, 529, true)]
    [InlineData(Provider.OpenAI, 529, false)]
    [InlineData(Provider.MicrosoftAI, 529, false)]
    [InlineData(Provider.OpenAI, 500, false)]
    [InlineData(Provider.OpenAI, 502, false)]
    [InlineData(Provider.OpenAI, 504, false)]
    [InlineData(Provider.Anthropic, 500, false)]
    [InlineData(Provider.OpenAI, 400, false)]
    [InlineData(Provider.OpenAI, 200, false)]
    public void IsSafeToReplay_OnlyForResponsesThatMeanTheRequestWasNotProcessed(Provider provider, int statusCode, bool expected) =>
        UpstreamFailureClassifier.IsSafeToReplay(provider, statusCode).Should().Be(expected);

    [Theory]
    [InlineData(SocketError.ConnectionRefused)]
    [InlineData(SocketError.HostUnreachable)]
    [InlineData(SocketError.NetworkUnreachable)]
    [InlineData(SocketError.AddressNotAvailable)]
    [InlineData(SocketError.HostNotFound)]
    public void FailedBeforeRequestWasSent_ForConnectionEstablishmentSocketErrors_IsTrue(SocketError socketError)
    {
        var exception = new HttpRequestException(
            HttpRequestError.ConnectionError, "connect failed", new SocketException((int)socketError));

        UpstreamFailureClassifier.FailedBeforeRequestWasSent(exception).Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    [InlineData(HttpRequestError.ProxyTunnelError)]
    public void FailedBeforeRequestWasSent_ForPreSendRequestErrors_IsTrue(HttpRequestError error) =>
        UpstreamFailureClassifier.FailedBeforeRequestWasSent(new HttpRequestException(error, "failed")).Should().BeTrue();

    [Fact]
    public void FailedBeforeRequestWasSent_ForConnectTimeout_IsTrue()
    {
        // SocketsHttpHandler reports ConnectTimeout as a TaskCanceledException wrapping a TimeoutException.
        var exception = new TaskCanceledException(
            "The operation was canceled.",
            new TimeoutException("A connection could not be established within the configured ConnectTimeout."));

        UpstreamFailureClassifier.FailedBeforeRequestWasSent(exception).Should().BeTrue();
    }

    [Theory]
    [InlineData(SocketError.ConnectionReset)]
    [InlineData(SocketError.ConnectionAborted)]
    [InlineData(SocketError.Shutdown)]
    [InlineData(SocketError.TimedOut)]
    public void FailedBeforeRequestWasSent_ForConnectionLostAfterConnecting_IsFalse(SocketError socketError)
    {
        // .NET also reports a connection reset after the request was sent as ConnectionError.
        var exception = new HttpRequestException(
            HttpRequestError.ConnectionError, "connection lost", new SocketException((int)socketError));

        UpstreamFailureClassifier.FailedBeforeRequestWasSent(exception).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpRequestError.ResponseEnded)]
    [InlineData(HttpRequestError.InvalidResponse)]
    [InlineData(HttpRequestError.HttpProtocolError)]
    [InlineData(HttpRequestError.Unknown)]
    public void FailedBeforeRequestWasSent_ForErrorsAfterConnecting_IsFalse(HttpRequestError error) =>
        UpstreamFailureClassifier.FailedBeforeRequestWasSent(new HttpRequestException(error, "failed")).Should().BeFalse();

    [Fact]
    public void FailedBeforeRequestWasSent_ForCancellationWithoutATimeout_IsFalse() =>
        UpstreamFailureClassifier.FailedBeforeRequestWasSent(new TaskCanceledException("canceled")).Should().BeFalse();

    [Fact]
    public void FailedBeforeRequestWasSent_ForConnectionErrorWithoutASocketCause_IsFalse() =>
        UpstreamFailureClassifier.FailedBeforeRequestWasSent(
            new HttpRequestException(HttpRequestError.ConnectionError, "no inner cause")).Should().BeFalse();
}
