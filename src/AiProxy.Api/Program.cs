using System.Net;
using AiProxy.Application.Abstractions;
using AiProxy.Application.Usage;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Routing;
using AiProxy.Infrastructure.Usage;

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

builder.Services.AddSingleton<UsageRecordChannel>();
builder.Services.AddHostedService<UsageRecordBackgroundWriter>();
builder.Services.AddSingleton<IUsageExtractorResolver, UsageExtractorResolver>();

foreach (var family in new[] { ApiFamily.OpenAI, ApiFamily.AzureOpenAI, ApiFamily.OpenAICompatible })
{
    foreach (var operation in new[] { OperationType.ChatCompletion, OperationType.Completion, OperationType.Embedding })
    {
        var extractor = new OpenAiStyleTokenUsageExtractor(family, operation);
        builder.Services.AddSingleton<IUsageExtractor>(extractor);
    }

    builder.Services.AddSingleton<IUsageExtractor>(new ResponsesUsageExtractor(family));
}

builder.Services.AddSingleton<IUsageExtractor, AnthropicChatUsageExtractor>();
builder.Services.AddSingleton<IUsageExtractor>(new ImageUsageExtractor(OperationType.ImageGeneration));
builder.Services.AddSingleton<IUsageExtractor>(new ImageUsageExtractor(OperationType.ImageEdit));
builder.Services.AddSingleton<IUsageExtractor>(new ImageUsageExtractor(OperationType.ImageVariation));

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
    // Placeholder until Phase 7 wires real ingress authentication (IConsumerAuthenticator resolving the
    // caller's API key to a Consumer) — until then usage capture has no identity to attribute usage to.
    var consumerId = httpContext.Request.Headers.TryGetValue("X-Consumer-Id", out var consumerIdHeader) &&
        Guid.TryParse(consumerIdHeader, out var parsedConsumerId)
            ? parsedConsumerId
            : (Guid?)null;
    var consumerName = httpContext.Request.Headers.TryGetValue("X-Consumer-Name", out var consumerNameHeader)
        ? consumerNameHeader.ToString()
        : null;

    var failure = await router.RouteAsync(httpContext, operationType, cancellationToken, consumerId, consumerName);
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
