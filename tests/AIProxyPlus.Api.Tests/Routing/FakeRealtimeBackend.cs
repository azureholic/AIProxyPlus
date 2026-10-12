using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AIProxyPlus.Api.Tests.Routing;

/// <summary>
/// A minimal, real Kestrel WebSocket backend used to exercise <c>RealtimeProxyService</c> end-to-end.
/// WireMock.Net (used for the HTTP backends elsewhere in these tests) has no WebSocket support, so this
/// stands in for "a real OpenAI Realtime backend": it echoes back whatever it receives, and can be told to
/// reject the handshake to simulate a backend that's down.
/// </summary>
public sealed class FakeRealtimeBackend : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _acceptedConnectionCount;

    public string Url { get; private set; } = "";

    public bool RejectHandshake { get; set; }

    public string? LastRequestQuery { get; private set; }

    public IReadOnlyDictionary<string, string> LastRequestHeaders { get; private set; } =
        new Dictionary<string, string>();

    public IReadOnlyList<string> ServerMessageFragments { get; set; } = [];

    public int AcceptedConnectionCount => _acceptedConnectionCount;

    private FakeRealtimeBackend(WebApplication app) => _app = app;

    public static async Task<FakeRealtimeBackend> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var backend = new FakeRealtimeBackend(app);

        app.UseWebSockets();
        app.Run(async context =>
        {
            backend.LastRequestQuery = context.Request.QueryString.Value;
            backend.LastRequestHeaders = context.Request.Headers.ToDictionary(
                header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            if (backend.RejectHandshake || !context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            Interlocked.Increment(ref backend._acceptedConnectionCount);
            for (var fragmentIndex = 0; fragmentIndex < backend.ServerMessageFragments.Count; fragmentIndex++)
            {
                var fragment = Encoding.UTF8.GetBytes(backend.ServerMessageFragments[fragmentIndex]);
                await socket.SendAsync(
                    fragment,
                    WebSocketMessageType.Text,
                    endOfMessage: fragmentIndex == backend.ServerMessageFragments.Count - 1,
                    context.RequestAborted);
            }

            var buffer = new byte[8192];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    break;
                }

                await socket.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, context.RequestAborted);
            }
        });

        await app.StartAsync();
        backend.Url = app.Urls.First();
        return backend;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
