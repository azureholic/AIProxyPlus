using System.Net;
using AiProxy.Application.Abstractions;
using AiProxy.Application.Consumers;
using AiProxy.Application.ModelDeployments;
using AiProxy.Application.Usage;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Observability;
using AiProxy.Infrastructure.Persistence;
using AiProxy.Infrastructure.Repositories;
using AiProxy.Infrastructure.Routing;
using AiProxy.Infrastructure.Secrets;
using AiProxy.Infrastructure.Security;
using AiProxy.Infrastructure.Usage;
using Azure.Core;
using Azure.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Enrichers.Span;
using VaultSharp;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithSpan()
    .Enrich.WithProperty("Application", "AiProxy")
    // EndpointCredential carries the outbound Authorization/api-key header value — never render it raw (architecture.md §13).
    .Destructure.ByTransforming<EndpointCredential>(c => new { c.HeaderName, HeaderValue = "[REDACTED]" })
    .WriteTo.Console()
    .WriteTo.File("logs/ai-proxy-.log", rollingInterval: RollingInterval.Day),
    writeToProviders: true); // also forward to the OTel logging provider registered by AddServiceDefaults, for OTLP export

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
builder.Services.AddSingleton<IEndpointRoundRobinCounter, EndpointRoundRobinCounter>();
builder.Services.AddSingleton<AiProxyMetrics>();

// Postgres (architecture.md §7.1) — the Aspire client integration resolves the "aiproxydb" connection string
// (injected by AiProxy.AppHost via WithReference) and wires resiliency/health checks/OTel automatically.
builder.AddNpgsqlDbContext<AiProxyDbContext>("aiproxydb");
builder.Services.AddScoped<IModelDeploymentRepository, ModelDeploymentRepository>();
builder.Services.AddScoped<IConsumerRepository, ConsumerRepository>();
builder.Services.AddScoped<ISessionAffinityStore, SessionAffinityStore>();
builder.Services.AddScoped<IUsageRecordRepository, UsageRecordRepository>();

// Vault (architecture.md §8) — address/token come from AiProxy.AppHost's Vault__Address/Vault__Token in dev.
builder.Services.AddSingleton(_ => new VaultOptions
{
    Address = builder.Configuration["Vault:Address"] ?? "http://localhost:8200",
    Token = builder.Configuration["Vault:Token"],
});
builder.Services.AddSingleton<IVaultClient>(sp => VaultClientFactory.Create(sp.GetRequiredService<VaultOptions>()));
builder.Services.AddSingleton<ISecretResolver, VaultSecretResolver>();

builder.Services.AddMemoryCache();
builder.Services.AddScoped<ApiKeyCredentialProvider>();
builder.Services.AddSingleton<TokenCredential>(new DefaultAzureCredential());
builder.Services.AddScoped<ManagedIdentityCredentialProvider>();
builder.Services.AddScoped<ICredentialProvider, CredentialProvider>();

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

// Secrets/auth — pepper and admin key hash are auto-provisioned from Vault on first read (§8); ISecretResolver
// itself isn't registered until Phase 9 wires a real Vault client (or a test double supplies one).
builder.Services.AddSingleton<IApiKeyHasher>(sp =>
    ApiKeyHasher.CreateAsync(sp.GetRequiredService<ISecretResolver>()).GetAwaiter().GetResult());
builder.Services.AddSingleton(sp =>
    AdminApiKeyAuthenticator.CreateAsync(sp.GetRequiredService<ISecretResolver>(), sp.GetRequiredService<ILogger<Program>>())
        .GetAwaiter().GetResult());
builder.Services.AddSingleton<IApiKeyGenerator, ApiKeyGenerator>();
builder.Services.AddScoped<IConsumerAuthenticator, ConsumerAuthenticator>();

// Configuration REST API use cases (§7.2)
builder.Services.AddSingleton<IValidator<CreateModelDeploymentCommand>, CreateModelDeploymentValidator>();
builder.Services.AddSingleton<IValidator<UpdateModelDeploymentCommand>, UpdateModelDeploymentValidator>();
builder.Services.AddSingleton<IValidator<DeleteModelDeploymentCommand>, DeleteModelDeploymentValidator>();
builder.Services.AddSingleton<IValidator<AddEndpointCommand>, AddEndpointValidator>();
builder.Services.AddSingleton<IValidator<UpdateEndpointCommand>, UpdateEndpointValidator>();
builder.Services.AddSingleton<IValidator<RemoveEndpointCommand>, RemoveEndpointValidator>();
builder.Services.AddSingleton<IValidator<SetEndpointSecretCommand>, SetEndpointSecretValidator>();
builder.Services.AddSingleton<IValidator<CreateConsumerCommand>, CreateConsumerValidator>();
builder.Services.AddSingleton<IValidator<RotateConsumerKeyCommand>, RotateConsumerKeyValidator>();
builder.Services.AddSingleton<IValidator<RevokeConsumerCommand>, RevokeConsumerValidator>();
builder.Services.AddSingleton<IValidator<UsageRecordQuery>, UsageRecordQueryValidator>();

builder.Services.AddScoped<CreateModelDeploymentHandler>();
builder.Services.AddScoped<UpdateModelDeploymentHandler>();
builder.Services.AddScoped<DeleteModelDeploymentHandler>();
builder.Services.AddScoped<AddEndpointHandler>();
builder.Services.AddScoped<UpdateEndpointHandler>();
builder.Services.AddScoped<RemoveEndpointHandler>();
builder.Services.AddScoped<SetEndpointSecretHandler>();
builder.Services.AddScoped<CreateConsumerHandler>();
builder.Services.AddScoped<RotateConsumerKeyHandler>();
builder.Services.AddScoped<RevokeConsumerHandler>();
builder.Services.AddScoped<QueryUsageRecordsHandler>();

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async httpContext =>
{
    var error = httpContext.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, message) = error switch
    {
        ValidationException validationException => (
            StatusCodes.Status400BadRequest, string.Join("; ", validationException.Errors.Select(e => e.ErrorMessage))),
        AiProxy.Application.Exceptions.NotFoundException notFoundException => (StatusCodes.Status404NotFound, notFoundException.Message),
        AiProxy.Application.Exceptions.ConflictException conflictException => (StatusCodes.Status409Conflict, conflictException.Message),
        AiProxy.Domain.Exceptions.DomainException domainException => (StatusCodes.Status400BadRequest, domainException.Message),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
    };
    httpContext.Response.StatusCode = status;
    await httpContext.Response.WriteAsJsonAsync(new { error = message });
}));

app.UseSerilogRequestLogging();

app.MapGet("/", () => "Hello World!");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", HealthChecks.CheckReadyAsync);

var ingress = app.MapGroup("").AddEndpointFilter<ConsumerAuthEndpointFilter>();

ingress.MapPost("/openai/v1/chat/completions", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.ChatCompletion, ct));
ingress.MapPost("/openai/v1/completions", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.Completion, ct));
ingress.MapPost("/openai/v1/embeddings", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.Embedding, ct));
ingress.MapPost("/openai/v1/images/generations", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.ImageGeneration, ct));
ingress.MapPost("/openai/v1/images/edits", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.ImageEdit, ct));
ingress.MapPost("/openai/v1/images/variations", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.ImageVariation, ct));
ingress.MapPost("/openai/v1/responses", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.ResponsesApi, ct));
ingress.MapPost("/anthropic/v1/messages", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.ChatCompletion, ct));

var config = app.MapGroup("/api/v1/config").AddEndpointFilter<AdminAuthEndpointFilter>();

config.MapGet("/model-deployments", async (IModelDeploymentRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetAllAsync(ct)));

config.MapGet("/model-deployments/{name}", async (string name, IModelDeploymentRepository repository, CancellationToken ct) =>
{
    var deployment = await repository.GetByNameAsync(name, ct);
    return deployment is null ? Results.NotFound(new { error = $"Model deployment '{name}' was not found." }) : Results.Ok(deployment);
});

config.MapPost("/model-deployments", async (CreateModelDeploymentCommand command, CreateModelDeploymentHandler handler, CancellationToken ct) =>
{
    var deployment = await handler.HandleAsync(command, ct);
    return Results.Created($"/api/v1/config/model-deployments/{deployment.Name}", deployment);
});

config.MapPut("/model-deployments/{name}", async (
    string name, UpdateModelDeploymentBody body, UpdateModelDeploymentHandler handler, CancellationToken ct) =>
{
    var deployment = await handler.HandleAsync(new UpdateModelDeploymentCommand(name, body.Strategy), ct);
    return Results.Ok(deployment);
});

config.MapDelete("/model-deployments/{name}", async (string name, DeleteModelDeploymentHandler handler, CancellationToken ct) =>
{
    await handler.HandleAsync(new DeleteModelDeploymentCommand(name), ct);
    return Results.NoContent();
});

config.MapPost("/model-deployments/{name}/endpoints", async (
    string name, AddEndpointBody body, AddEndpointHandler handler, CancellationToken ct) =>
{
    var endpoint = await handler.HandleAsync(
        new AddEndpointCommand(
            name, body.BaseUrl, body.ApiFamily, body.Priority, body.AuthMode, body.ApiKeyPlaintext,
            body.ManagedIdentityScope, body.Enabled, body.TimeoutSeconds, body.MaxRetries),
        ct);
    return Results.Created($"/api/v1/config/model-deployments/{name}/endpoints/{endpoint.Id}", endpoint);
});

config.MapPut("/model-deployments/{name}/endpoints/{id:guid}", async (
    string name, Guid id, UpdateEndpointBody body, UpdateEndpointHandler handler, CancellationToken ct) =>
{
    var endpoint = await handler.HandleAsync(
        new UpdateEndpointCommand(name, id, body.Priority, body.Enabled, body.TimeoutSeconds, body.MaxRetries), ct);
    return Results.Ok(endpoint);
});

config.MapDelete("/model-deployments/{name}/endpoints/{id:guid}", async (
    string name, Guid id, RemoveEndpointHandler handler, CancellationToken ct) =>
{
    await handler.HandleAsync(new RemoveEndpointCommand(name, id), ct);
    return Results.NoContent();
});

config.MapPut("/model-deployments/{name}/endpoints/{id:guid}/secret", async (
    string name, Guid id, SetEndpointSecretBody body, SetEndpointSecretHandler handler, CancellationToken ct) =>
{
    await handler.HandleAsync(new SetEndpointSecretCommand(name, id, body.ApiKeyPlaintext), ct);
    return Results.NoContent();
});

config.MapGet("/consumers", async (IConsumerRepository repository, CancellationToken ct) =>
    Results.Ok((await repository.GetAllAsync(ct)).Select(ConsumerResponse.From)));

config.MapPost("/consumers", async (CreateConsumerCommand command, CreateConsumerHandler handler, CancellationToken ct) =>
{
    var result = await handler.HandleAsync(command, ct);
    return Results.Created(
        $"/api/v1/config/consumers/{result.Consumer.Name}",
        new { consumer = ConsumerResponse.From(result.Consumer), apiKey = result.PlaintextApiKey });
});

config.MapPut("/consumers/{name}/rotate", async (string name, RotateConsumerKeyHandler handler, CancellationToken ct) =>
{
    var result = await handler.HandleAsync(new RotateConsumerKeyCommand(name), ct);
    return Results.Ok(new { consumer = ConsumerResponse.From(result.Consumer), apiKey = result.PlaintextApiKey });
});

config.MapDelete("/consumers/{name}", async (string name, RevokeConsumerHandler handler, CancellationToken ct) =>
{
    var consumer = await handler.HandleAsync(new RevokeConsumerCommand(name), ct);
    return Results.Ok(ConsumerResponse.From(consumer));
});

app.MapGroup("/api/v1")
    .AddEndpointFilter<AdminAuthEndpointFilter>()
    .MapGet("/usage", async (
        string? modelDeploymentName, string? consumerName, OperationType? operationType, DateTimeOffset? from, DateTimeOffset? to,
        int? page, int? pageSize, QueryUsageRecordsHandler handler, CancellationToken ct) =>
    {
        var query = new UsageRecordQuery(modelDeploymentName, consumerName, operationType, from, to, page ?? 1, pageSize ?? 50);
        return Results.Ok(await handler.HandleAsync(query, ct));
    });

app.Run();

static async Task RouteOrFailAsync(
    HttpContext httpContext, ProxyRoutingService router, OperationType operationType, CancellationToken cancellationToken)
{
    var consumerId = httpContext.Items.TryGetValue(ConsumerAuthEndpointFilter.ConsumerIdItemKey, out var idValue) ? (Guid?)idValue : null;
    var consumerName = httpContext.Items.TryGetValue(ConsumerAuthEndpointFilter.ConsumerNameItemKey, out var nameValue)
        ? (string?)nameValue
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

/// <summary>Probes Postgres/Vault reachability via the registered ports (architecture.md §12).</summary>
static class HealthChecks
{
    public static async Task<IResult> CheckReadyAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var checks = new Dictionary<string, string>
        {
            ["vault"] = await ProbeAsync(() =>
                services.GetRequiredService<ISecretResolver>().TryResolveAsync("__health_check__", cancellationToken)),
            ["postgres"] = await ProbeAsync(() => services.GetRequiredService<IModelDeploymentRepository>().GetAllAsync(cancellationToken)),
        };

        var allHealthy = checks.Values.All(v => v == "healthy");
        return allHealthy ? Results.Ok(checks) : Results.Json(checks, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<string> ProbeAsync(Func<Task> probe)
    {
        try
        {
            await probe();
            return "healthy";
        }
        catch (Exception ex)
        {
            return $"unhealthy: {ex.Message}";
        }
    }
}

public sealed record UpdateModelDeploymentBody(RoutingStrategy Strategy);

public sealed record AddEndpointBody(
    string BaseUrl,
    ApiFamily ApiFamily,
    int Priority,
    AuthMode AuthMode,
    string? ApiKeyPlaintext = null,
    string? ManagedIdentityScope = null,
    bool Enabled = true,
    int TimeoutSeconds = 100,
    int MaxRetries = 2);

public sealed record UpdateEndpointBody(int? Priority = null, bool? Enabled = null, int? TimeoutSeconds = null, int? MaxRetries = null);

public sealed record SetEndpointSecretBody(string ApiKeyPlaintext);

public sealed record ConsumerResponse(Guid Id, string Name, bool Enabled, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt)
{
    public static ConsumerResponse From(Consumer consumer) => new(consumer.Id, consumer.Name, consumer.Enabled, consumer.CreatedAt, consumer.RevokedAt);
}

/// <summary>Exposes the top-level program to <c>WebApplicationFactory&lt;Program&gt;</c> in tests.</summary>
public partial class Program;
