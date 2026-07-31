using System.Net;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Routing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new HttpMessageInvoker(new SocketsHttpHandler
{
    UseProxy = false,
    AllowAutoRedirect = false,
    AutomaticDecompression = DecompressionMethods.None,
    UseCookies = false,
    EnableMultipleHttp2Connections = true,
    ConnectTimeout = TimeSpan.FromSeconds(15),
}));
builder.Services.AddSingleton<EndpointHealthTracker>();
builder.Services.AddSingleton<AiProxy.Application.Abstractions.IEndpointHealthTracker>(
    sp => sp.GetRequiredService<EndpointHealthTracker>());
builder.Services.AddScoped<ProxyRoutingService>();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapPost("/openai/v1/chat/completions", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.ChatCompletion, ct));
app.MapPost("/openai/v1/completions", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.Completion, ct));
app.MapPost("/openai/v1/embeddings", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.Embedding, ct));
app.MapPost("/openai/v1/images/generations", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.ImageGeneration, ct));
app.MapPost("/openai/v1/images/edits", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.ImageEdit, ct));
app.MapPost("/openai/v1/images/variations", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.ImageVariation, ct));
app.MapPost("/openai/v1/responses", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.ResponsesApi, ct));
app.MapPost("/anthropic/v1/messages", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.ChatCompletion, ct));

app.Run();

static async Task RouteOrFailAsync(
    HttpContext httpContext, ProxyRoutingService router, OperationType operationType, CancellationToken cancellationToken)
{
    var failure = await router.RouteAsync(httpContext, operationType, cancellationToken);
    if (failure is null)
    {
        return;
    }

    httpContext.Response.StatusCode = failure.Reason switch
    {
        RoutingFailureReason.DeploymentNotFound => StatusCodes.Status404NotFound,
        RoutingFailureReason.NoHealthyEndpoint => StatusCodes.Status503ServiceUnavailable,
        RoutingFailureReason.StickyEndpointUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };
    await httpContext.Response.WriteAsJsonAsync(new { error = failure.Message });
}

/// <summary>Exposes the top-level program to <c>WebApplicationFactory&lt;Program&gt;</c> in tests.</summary>
public partial class Program;
