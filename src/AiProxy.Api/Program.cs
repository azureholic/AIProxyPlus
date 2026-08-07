using System.Net;
using AiProxy.Application.Abstractions;
using AiProxy.Application.ApiRoutes;
using AiProxy.Application.Consumers;
using AiProxy.Application.Endpoints;
using AiProxy.Application.Models;
using AiProxy.Application.Usage;
using AiProxy.Api.Authentication;
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
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Enrichers.Span;
using VaultSharp;

var builder = WebApplication.CreateBuilder(args);

var oidcOptions = builder.Configuration.GetSection(OidcAuthenticationOptions.SectionName)
    .Get<OidcAuthenticationOptions>() ?? new OidcAuthenticationOptions();
oidcOptions.Validate();

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
builder.Services.AddSingleton(_ =>
{
    var cooldown = builder.Configuration.GetValue<TimeSpan?>("Routing:UnhealthyCooldown");
    return new EndpointHealthTracker(cooldown);
});
builder.Services.AddSingleton<AiProxy.Application.Abstractions.IEndpointHealthTracker>(
    sp => sp.GetRequiredService<EndpointHealthTracker>());
builder.Services.AddSingleton<IEndpointRoundRobinCounter, EndpointRoundRobinCounter>();
builder.Services.AddSingleton<AiProxyMetrics>();

// Postgres (architecture.md §7.1) — the Aspire client integration resolves the "aiproxydb" connection string
// (injected by AiProxy.AppHost via WithReference) and wires resiliency/health checks/OTel automatically.
builder.AddNpgsqlDbContext<AiProxyDbContext>("aiproxydb");
builder.Services.AddScoped<IModelRepository, ModelRepository>();
builder.Services.AddScoped<IEndpointRepository, EndpointRepository>();
builder.Services.AddScoped<IApiRouteRepository, ApiRouteRepository>();
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
builder.Services.AddScoped<IEndpointCredentialCache>(services => services.GetRequiredService<ApiKeyCredentialProvider>());
builder.Services.AddSingleton<TokenCredential>(new DefaultAzureCredential());
builder.Services.AddScoped<ManagedIdentityCredentialProvider>();
builder.Services.AddScoped<ICredentialProvider, CredentialProvider>();

builder.Services.AddSingleton<UsageRecordChannel>();
builder.Services.AddHostedService<UsageRecordBackgroundWriter>();
builder.Services.AddSingleton<IUsageExtractorResolver, UsageExtractorResolver>();

foreach (var provider in new[] { Provider.OpenAI, Provider.MicrosoftAI })
{
    foreach (var operation in new[] { OperationType.ChatCompletion, OperationType.Completion, OperationType.Embedding })
    {
        var extractor = new OpenAiStyleTokenUsageExtractor(provider, operation);
        builder.Services.AddSingleton<IUsageExtractor>(extractor);
    }

    builder.Services.AddSingleton<IUsageExtractor>(new ResponsesUsageExtractor(provider));
}

builder.Services.AddSingleton<IUsageExtractor, AnthropicChatUsageExtractor>();
foreach (var provider in new[] { Provider.OpenAI, Provider.MicrosoftAI })
{
    builder.Services.AddSingleton<IUsageExtractor>(new ImageUsageExtractor(provider, OperationType.ImageGeneration));
    builder.Services.AddSingleton<IUsageExtractor>(new ImageUsageExtractor(provider, OperationType.ImageEdit));
    builder.Services.AddSingleton<IUsageExtractor>(new ImageUsageExtractor(provider, OperationType.ImageVariation));
}

builder.Services.AddSingleton<UpstreamRouteResolver>();
builder.Services.AddScoped<ProxyRoutingService>(sp => new ProxyRoutingService(
    sp.GetRequiredService<IModelRepository>(),
    sp.GetRequiredService<ISessionAffinityStore>(),
    sp.GetRequiredService<AiProxy.Application.Abstractions.IEndpointHealthTracker>(),
    sp.GetRequiredService<IEndpointRoundRobinCounter>(),
    sp.GetRequiredService<ICredentialProvider>(),
    sp.GetRequiredService<UpstreamRouteResolver>(),
    sp.GetRequiredService<IUsageExtractorResolver>(),
    sp.GetRequiredService<UsageRecordChannel>(),
    sp.GetRequiredService<AiProxyMetrics>(),
    sp.GetRequiredService<HttpMessageInvoker>(),
    builder.Configuration.GetValue<TimeSpan?>("Routing:SessionAffinityTtl")));
builder.Services.AddScoped<RealtimeProxyService>();

// Secrets/auth — pepper and admin key hash are auto-provisioned from Vault on first read (§8); ISecretResolver
// itself isn't registered until Phase 9 wires a real Vault client (or a test double supplies one).
builder.Services.AddSingleton<IApiKeyHasher>(sp =>
    ApiKeyHasher.CreateAsync(sp.GetRequiredService<ISecretResolver>()).GetAwaiter().GetResult());
builder.Services.AddSingleton(sp =>
    AdminApiKeyAuthenticator.CreateAsync(sp.GetRequiredService<ISecretResolver>(), sp.GetRequiredService<ILogger<Program>>())
        .GetAwaiter().GetResult());
builder.Services.AddSingleton<IApiKeyGenerator, ApiKeyGenerator>();
builder.Services.AddScoped<IConsumerAuthenticator, ConsumerAuthenticator>();
builder.Services.AddScoped<IOidcConsumerResolver, OidcConsumerResolver>();
builder.Services.AddScoped<IRealtimeTicketService, RealtimeTicketService>();

var authentication = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "__Host-AiProxy.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = false;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(oidcOptions.SessionLifetimeMinutes);
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AdminApiKeyAuthenticationHandler>(
        AdminApiKeyAuthenticationHandler.SchemeName,
        _ => { });

if (oidcOptions.Enabled)
{
    authentication.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.Authority = oidcOptions.Authority.TrimEnd('/');
        options.RequireHttpsMetadata = !(
            builder.Environment.IsDevelopment() &&
            Uri.TryCreate(oidcOptions.Authority, UriKind.Absolute, out var authority) &&
            authority.IsLoopback);
        options.ClientId = oidcOptions.ClientId;
        options.ClientSecret = oidcOptions.ClientSecret;
        options.ResponseType = "code";
        options.UsePkce = true;
        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = oidcOptions.GetClaimsFromUserInfo;
        options.CallbackPath = oidcOptions.CallbackPath;
        options.SignedOutCallbackPath = oidcOptions.SignedOutCallbackPath;
        options.SaveTokens = false;
        options.Scope.Clear();
        foreach (var scope in oidcOptions.Scopes)
        {
            options.Scope.Add(scope);
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = oidcOptions.UsernameClaim,
            ValidAlgorithms = oidcOptions.AllowedSigningAlgorithms,
        };
        options.Events.OnTicketReceived = context =>
        {
            var issuer = options.Configuration?.Issuer ?? options.Authority;

            if (context.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity &&
                !identity.HasClaim(claim => claim.Type == "iss") &&
                !string.IsNullOrWhiteSpace(issuer))
            {
                identity.AddClaim(new System.Security.Claims.Claim("iss", issuer.TrimEnd('/')));
            }

            return Task.CompletedTask;
        };
        options.Events.OnRedirectToIdentityProviderForSignOut = context =>
        {
            context.ProtocolMessage.ClientId = options.ClientId;
            return Task.CompletedTask;
        };
    });
}

builder.Services.AddAuthorization(options => options.AddPolicy("AdminAccess", policy =>
{
    policy.AddAuthenticationSchemes(
        CookieAuthenticationDefaults.AuthenticationScheme,
        AdminApiKeyAuthenticationHandler.SchemeName);
    policy.RequireAuthenticatedUser();
}));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

// Configuration REST API use cases (§7.2)
builder.Services.AddSingleton<IValidator<CreateModelCommand>, CreateModelValidator>();
builder.Services.AddSingleton<IValidator<UpdateModelCommand>, UpdateModelValidator>();
builder.Services.AddSingleton<IValidator<DeleteModelCommand>, DeleteModelValidator>();
builder.Services.AddSingleton<IValidator<AddEndpointCommand>, AddEndpointValidator>();
builder.Services.AddSingleton<IValidator<UpdateEndpointCommand>, UpdateEndpointValidator>();
builder.Services.AddSingleton<IValidator<RemoveEndpointCommand>, RemoveEndpointValidator>();
builder.Services.AddSingleton<IValidator<CreateEndpointCommand>, CreateEndpointValidator>();
builder.Services.AddSingleton<IValidator<UpdateEndpointConfigurationCommand>, UpdateEndpointConfigurationValidator>();
builder.Services.AddSingleton<IValidator<DeleteEndpointCommand>, DeleteEndpointValidator>();
builder.Services.AddSingleton<IValidator<SetEndpointSecretCommand>, SetEndpointSecretValidator>();
builder.Services.AddSingleton<IValidator<CreateApiRouteCommand>, CreateApiRouteValidator>();
builder.Services.AddSingleton<IValidator<UpdateApiRouteCommand>, UpdateApiRouteValidator>();
builder.Services.AddSingleton<IValidator<DeleteApiRouteCommand>, DeleteApiRouteValidator>();
builder.Services.AddSingleton<IValidator<CreateConsumerCommand>, CreateConsumerValidator>();
builder.Services.AddSingleton<IValidator<RotateConsumerKeyCommand>, RotateConsumerKeyValidator>();
builder.Services.AddSingleton<IValidator<DisableConsumerCommand>, DisableConsumerValidator>();
builder.Services.AddSingleton<IValidator<EnableConsumerCommand>, EnableConsumerValidator>();
builder.Services.AddSingleton<IValidator<RevokeConsumerCommand>, RevokeConsumerValidator>();
builder.Services.AddSingleton<IValidator<DeleteConsumerPermanentlyCommand>, DeleteConsumerPermanentlyValidator>();
builder.Services.AddSingleton<IValidator<AllowConsumerModelCommand>, AllowConsumerModelValidator>();
builder.Services.AddSingleton<IValidator<RevokeConsumerModelCommand>, RevokeConsumerModelValidator>();
builder.Services.AddSingleton<IValidator<UsageRecordQuery>, UsageRecordQueryValidator>();

builder.Services.AddScoped<CreateModelHandler>();
builder.Services.AddScoped<UpdateModelHandler>();
builder.Services.AddScoped<DeleteModelHandler>();
builder.Services.AddScoped<AddEndpointHandler>();
builder.Services.AddScoped<UpdateEndpointHandler>();
builder.Services.AddScoped<RemoveEndpointHandler>();
builder.Services.AddScoped<CreateEndpointHandler>();
builder.Services.AddScoped<UpdateEndpointConfigurationHandler>();
builder.Services.AddScoped<DeleteEndpointHandler>();
builder.Services.AddScoped<SetEndpointSecretHandler>();
builder.Services.AddScoped<CreateApiRouteHandler>();
builder.Services.AddScoped<UpdateApiRouteHandler>();
builder.Services.AddScoped<DeleteApiRouteHandler>();
builder.Services.AddScoped<GetApiRoutesHandler>();
builder.Services.AddScoped<GetApiRouteHandler>();
builder.Services.AddScoped<CreateConsumerHandler>();
builder.Services.AddScoped<RotateConsumerKeyHandler>();
builder.Services.AddScoped<DisableConsumerHandler>();
builder.Services.AddScoped<EnableConsumerHandler>();
builder.Services.AddScoped<RevokeConsumerHandler>();
builder.Services.AddScoped<DeleteConsumerPermanentlyHandler>();
builder.Services.AddScoped<AllowConsumerModelHandler>();
builder.Services.AddScoped<RevokeConsumerModelHandler>();
builder.Services.AddScoped<QueryUsageRecordsHandler>();

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<AuthenticationOpenApiTransformer>());
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

if (builder.Configuration.GetValue("Database:ApplyMigrations", true))
{
    // Least-privilege support (architecture.md §13): when a distinct, more-privileged connection string is
    // configured for migrations, use it only for this startup DDL step — the regular DI-registered
    // AiProxyDbContext (used by every runtime query) keeps using the standard, narrower-scoped connection
    // string. Falls back to the single-connection-string behavior when the migrations key isn't set.
    var migrationsConnectionString = builder.Configuration.GetConnectionString("aiproxydb-migrations");
    if (string.IsNullOrWhiteSpace(migrationsConnectionString))
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AiProxyDbContext>().Database.MigrateAsync();
    }
    else
    {
        var migrationsOptions = new DbContextOptionsBuilder<AiProxyDbContext>().UseNpgsql(migrationsConnectionString).Options;
        await using var migrationsContext = new AiProxyDbContext(migrationsOptions);
        await migrationsContext.Database.MigrateAsync();
    }
}

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
app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", HealthChecks.CheckReadyAsync);

var ingress = app.MapGroup("")
    .AddEndpointFilter<SessionAntiforgeryEndpointFilter>()
    .AddEndpointFilter<ConsumerAuthEndpointFilter>();

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
ingress.MapPost("/openai/v1/audio/transcriptions", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.SpeechToText, ct));
ingress.MapPost("/openai/v1/audio/speech", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.TextToSpeech, ct));
ingress.MapPost("/anthropic/v1/messages", (HttpContext ctx, ProxyRoutingService router, CancellationToken ct) =>
    RouteOrFailAsync(ctx, router, OperationType.MessagesApi, ct));
ingress.MapMethods(
    "/openai/v1/realtime",
    [HttpMethods.Get, HttpMethods.Connect],
    async (HttpContext ctx, RealtimeProxyService router, CancellationToken ct) =>
{
    var allowedModelIds = ctx.Items.TryGetValue(ConsumerAuthEndpointFilter.AllowedModelIdsItemKey, out var allowedValue)
        ? (IReadOnlySet<Guid>?)allowedValue
        : null;
    var consumerId = ctx.Items.TryGetValue(ConsumerAuthEndpointFilter.ConsumerIdItemKey, out var idValue)
        ? (Guid?)idValue
        : null;
    var consumerName = ctx.Items.TryGetValue(ConsumerAuthEndpointFilter.ConsumerNameItemKey, out var nameValue)
        ? (string?)nameValue
        : null;
    var failure = await router.ProxyAsync(ctx, ct, consumerId, allowedModelIds, consumerName);
    if (failure is not null)
    {
        await WriteRoutingFailureAsync(ctx, failure);
    }
}).AddEndpointFilter<SameOriginEndpointFilter>();

var config = app.MapGroup("/api/v1/config")
    .RequireAuthorization("AdminAccess")
    .AddEndpointFilter<SessionAntiforgeryEndpointFilter>();

config.MapGet("/models", async (IModelRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetAllAsync(ct)));

config.MapGet("/models/{name}", async (string name, IModelRepository repository, CancellationToken ct) =>
{
    var model = await repository.GetByNameAsync(name, ct);
    return model is null ? Results.NotFound(new { error = $"Model '{name}' was not found." }) : Results.Ok(model);
});

config.MapPost("/models", async (CreateModelCommand command, CreateModelHandler handler, CancellationToken ct) =>
{
    var model = await handler.HandleAsync(command, ct);
    return Results.Created($"/api/v1/config/models/{model.Name}", model);
});

config.MapPut("/models/{name}", async (
    string name, UpdateModelBody body, UpdateModelHandler handler, CancellationToken ct) =>
{
    var model = await handler.HandleAsync(new UpdateModelCommand(name, body.Strategy, body.Capabilities), ct);
    return Results.Ok(model);
});

config.MapDelete("/models/{name}", async (string name, DeleteModelHandler handler, CancellationToken ct) =>
{
    await handler.HandleAsync(new DeleteModelCommand(name), ct);
    return Results.NoContent();
});

config.MapPost("/models/{name}/endpoints", async (
    string name, AddEndpointBody body, AddEndpointHandler handler, CancellationToken ct) =>
{
    var endpoint = await handler.HandleAsync(
        new AddEndpointCommand(name, body.EndpointId, body.ApiRouteId, body.Priority, body.Enabled),
        ct);
    return Results.Created($"/api/v1/config/models/{name}/endpoints/{endpoint.Id}", endpoint);
});

config.MapPut("/models/{name}/endpoints/{id:guid}", async (
    string name, Guid id, UpdateEndpointBody body, UpdateEndpointHandler handler, CancellationToken ct) =>
{
    var endpoint = await handler.HandleAsync(
        new UpdateEndpointCommand(name, id, body.Priority, body.Enabled), ct);
    return Results.Ok(endpoint);
});

config.MapDelete("/models/{name}/endpoints/{id:guid}", async (
    string name, Guid id, RemoveEndpointHandler handler, CancellationToken ct) =>
{
    await handler.HandleAsync(new RemoveEndpointCommand(name, id), ct);
    return Results.NoContent();
});

config.MapGet("/endpoints", async (IEndpointRepository repository, CancellationToken ct) =>
    Results.Ok(await repository.GetAllAsync(ct)));

config.MapGet("/endpoints/{id:guid}", async (Guid id, IEndpointRepository repository, CancellationToken ct) =>
{
    var endpoint = await repository.GetByIdAsync(id, ct);
    return endpoint is null
        ? Results.NotFound(new { error = $"Endpoint '{id}' was not found." })
        : Results.Ok(endpoint);
});

config.MapPost("/endpoints", async (
    CreateEndpointCommand command, CreateEndpointHandler handler, CancellationToken ct) =>
{
    var endpoint = await handler.HandleAsync(command, ct);
    return Results.Created($"/api/v1/config/endpoints/{endpoint.Id}", endpoint);
});

config.MapPut("/endpoints/{id:guid}", async (
    Guid id, UpdateEndpointConfigurationBody body, UpdateEndpointConfigurationHandler handler, CancellationToken ct) =>
{
    var endpoint = await handler.HandleAsync(new UpdateEndpointConfigurationCommand(
        id,
        body.Name,
        body.BaseUrl,
        body.ManagedIdentityScope,
        body.TimeoutSeconds,
        body.MaxRetries,
        body.ManagedIdentityTenantId), ct);
    return Results.Ok(endpoint);
});

config.MapDelete("/endpoints/{id:guid}", async (
    Guid id, DeleteEndpointHandler handler, CancellationToken ct) =>
{
    await handler.HandleAsync(new DeleteEndpointCommand(id), ct);
    return Results.NoContent();
});

config.MapPut("/endpoints/{id:guid}/secret", async (
    Guid id, SetEndpointSecretBody body, SetEndpointSecretHandler handler, CancellationToken ct) =>
{
    await handler.HandleAsync(new SetEndpointSecretCommand(id, body.ApiKeyPlaintext), ct);
    return Results.NoContent();
});

config.MapGet("/api-routes", async (GetApiRoutesHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(ct)))
    .WithName("ListApiRoutes")
    .WithSummary("Lists reusable provider API routes")
    .Produces<IReadOnlyList<ApiRoute>>();

config.MapGet("/api-routes/{id:guid}", async (Guid id, GetApiRouteHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.HandleAsync(id, ct)))
    .WithName("GetApiRoute")
    .WithSummary("Gets a reusable provider API route")
    .Produces<ApiRoute>()
    .ProducesProblem(StatusCodes.Status404NotFound);

config.MapPost("/api-routes", async (
        CreateApiRouteCommand command, CreateApiRouteHandler handler, CancellationToken ct) =>
    {
        var route = await handler.HandleAsync(command, ct);
        return Results.Created($"/api/v1/config/api-routes/{route.Id}", route);
    })
    .WithName("CreateApiRoute")
    .WithSummary("Creates a reusable provider API route")
    .Produces<ApiRoute>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status409Conflict);

config.MapPut("/api-routes/{id:guid}", async (
        Guid id, ApiRouteBody body, UpdateApiRouteHandler handler, CancellationToken ct) =>
    {
        var route = await handler.HandleAsync(
            new UpdateApiRouteCommand(id, body.Name, body.Provider, body.PathPrefix), ct);
        return Results.Ok(route);
    })
    .WithName("UpdateApiRoute")
    .WithSummary("Updates a reusable provider API route")
    .Produces<ApiRoute>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);

config.MapDelete("/api-routes/{id:guid}", async (
        Guid id, DeleteApiRouteHandler handler, CancellationToken ct) =>
    {
        await handler.HandleAsync(new DeleteApiRouteCommand(id), ct);
        return Results.NoContent();
    })
    .WithName("DeleteApiRoute")
    .WithSummary("Deletes an unassigned reusable provider API route")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);

config.MapGet("/consumers", async (IConsumerRepository repository, CancellationToken ct) =>
    Results.Ok((await repository.GetAllAsync(ct)).Select(ConsumerResponse.From)));

config.MapPost("/consumers", async (CreateConsumerCommand command, CreateConsumerHandler handler, CancellationToken ct) =>
{
    var result = await handler.HandleAsync(command, ct);
    return Results.Created(
        $"/api/v1/config/consumers/{result.Consumer.Name}",
        new { consumer = ConsumerResponse.From(result.Consumer), apiKey1 = result.ApiKey1, apiKey2 = result.ApiKey2 });
});

config.MapPut("/consumers/{name}/rotate", async (
    string name, int? slot, RotateConsumerKeyHandler handler, CancellationToken ct) =>
{
    if (slot is not 1 and not 2)
    {
        return Results.BadRequest(new { error = "Query parameter 'slot' is required and must be 1 or 2." });
    }

    var keySlot = slot == 1 ? ApiKeySlot.Primary : ApiKeySlot.Secondary;
    var result = await handler.HandleAsync(new RotateConsumerKeyCommand(name, keySlot), ct);
    return Results.Ok(new { consumer = ConsumerResponse.From(result.Consumer), slot, apiKey = result.PlaintextApiKey });
});

config.MapPut("/consumers/{name}/disable", async (
        string name, DisableConsumerHandler handler, CancellationToken ct) =>
    {
        var consumer = await handler.HandleAsync(new DisableConsumerCommand(name), ct);
        return Results.Ok(ConsumerResponse.From(consumer));
    })
    .WithName("DisableConsumer")
    .WithSummary("Reversibly disables consumer inference access")
    .Produces<ConsumerResponse>()
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);

config.MapPut("/consumers/{name}/enable", async (
        string name, EnableConsumerHandler handler, CancellationToken ct) =>
    {
        var consumer = await handler.HandleAsync(new EnableConsumerCommand(name), ct);
        return Results.Ok(ConsumerResponse.From(consumer));
    })
    .WithName("EnableConsumer")
    .WithSummary("Re-enables a disabled, non-revoked consumer")
    .Produces<ConsumerResponse>()
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);

config.MapPut("/consumers/{name}/allowed-models/{modelName}", async (
    string name, string modelName, AllowConsumerModelHandler handler, CancellationToken ct) =>
{
    var consumer = await handler.HandleAsync(new AllowConsumerModelCommand(name, modelName), ct);
    return Results.Ok(ConsumerResponse.From(consumer));
});

config.MapDelete("/consumers/{name}/allowed-models/{modelName}", async (
    string name, string modelName, RevokeConsumerModelHandler handler, CancellationToken ct) =>
{
    var consumer = await handler.HandleAsync(new RevokeConsumerModelCommand(name, modelName), ct);
    return Results.Ok(ConsumerResponse.From(consumer));
});

config.MapDelete("/consumers/{name}", async (string name, RevokeConsumerHandler handler, CancellationToken ct) =>
{
    var consumer = await handler.HandleAsync(new RevokeConsumerCommand(name), ct);
    return Results.Ok(ConsumerResponse.From(consumer));
});

config.MapDelete("/consumers/{name}/permanent", async (
        string name, DeleteConsumerPermanentlyHandler handler, CancellationToken ct) =>
    {
        await handler.HandleAsync(new DeleteConsumerPermanentlyCommand(name), ct);
        return Results.NoContent();
    })
    .WithName("DeleteConsumerPermanently")
    .WithSummary("Permanently deletes an already-revoked consumer")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);

app.MapGroup("/api/v1")
    .RequireAuthorization("AdminAccess")
    .MapGet("/usage", async (
        string? modelName, string? consumerName, OperationType? operationType, DateTimeOffset? from, DateTimeOffset? to,
        int? page, int? pageSize, QueryUsageRecordsHandler handler, CancellationToken ct) =>
    {
        var query = new UsageRecordQuery(modelName, consumerName, operationType, from, to, page ?? 1, pageSize ?? 50);
        return Results.Ok(await handler.HandleAsync(query, ct));
    });

var authenticationEndpoints = app.MapGroup("/api/v1/auth");

authenticationEndpoints.MapGet("/session", (HttpContext context) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        authenticated = true,
        username = context.User.FindFirst(oidcOptions.UsernameClaim)?.Value ?? context.User.Identity.Name,
        issuer = context.User.FindFirst("iss")?.Value,
    });
});

authenticationEndpoints.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
    {
        return Results.Unauthorized();
    }

    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { token = tokens.RequestToken });
});

authenticationEndpoints.MapPost("/realtime-ticket", async (
    HttpContext context,
    IOidcConsumerResolver consumerResolver,
    IRealtimeTicketService ticketService,
    CancellationToken cancellationToken) =>
{
    var issuer = context.User.FindFirst("iss")?.Value;
    var subject = context.User.FindFirst("sub")?.Value;
    var username = context.User.Identity?.Name;
    if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(username))
    {
        return Results.Unauthorized();
    }

    var consumer = await consumerResolver.ResolveAsync(
        new OidcConsumerIdentity(issuer, subject, username),
        cancellationToken);
    if (!consumer.Enabled)
    {
        return Results.Json(new { error = "This consumer is disabled." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var ticket = await ticketService.IssueAsync(
        consumer,
        RealtimeTicketRoutes.OpenAiRealtime,
        TimeSpan.FromSeconds(30),
        cancellationToken);
    return Results.Ok(new RealtimeTicketResponse(ticket, 30));
})
    .RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute
    {
        AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
    })
    .AddEndpointFilter<SameOriginEndpointFilter>()
    .AddEndpointFilter<SessionAntiforgeryEndpointFilter>();

authenticationEndpoints.MapGet("/login", (string? returnUrl) =>
{
    if (!oidcOptions.Enabled)
    {
        return Results.Problem("OIDC authentication is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var destination = IsSafeReturnUrl(returnUrl) ? returnUrl! : "/";
    return Results.Challenge(
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = destination },
        [OpenIdConnectDefaults.AuthenticationScheme]);
}).AddEndpointFilter<SameOriginEndpointFilter>();

authenticationEndpoints.MapPost("/logout", (string? returnUrl) =>
{
    var destination = IsSafeReturnUrl(returnUrl) ? returnUrl! : "/";
    var schemes = oidcOptions.Enabled
        ? new[] { CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme }
        : new[] { CookieAuthenticationDefaults.AuthenticationScheme };
    return Results.SignOut(
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = destination },
        schemes);
})
    .AddEndpointFilter<SameOriginEndpointFilter>()
    .AddEndpointFilter<SessionAntiforgeryEndpointFilter>();

// Frontend (Iteration 3, architecture.md §19): a built React app in wwwroot owns "/" and every client-side
// route via this fallback; falls back to a plain-text hint instead of a bare 404 if it hasn't been built yet.
// Registered last so it never shadows the API routes above — endpoint routing always prefers the more
// specific match regardless of registration order, but this ordering also keeps intent obvious in the file.
if (File.Exists(Path.Combine(app.Environment.WebRootPath, "index.html")))
{
    app.MapFallbackToFile("index.html");
}
else
{
    app.MapFallback(() => Results.Text(
        "AiProxy frontend not built yet. Run 'npm run build' in src/AiProxy.Web (or 'npm run dev' for local " +
        "frontend development against this API) to populate wwwroot.",
        "text/plain"));
}

app.Run();

static async Task RouteOrFailAsync(
    HttpContext httpContext, ProxyRoutingService router, OperationType operationType, CancellationToken cancellationToken)
{
    var consumerId = httpContext.Items.TryGetValue(ConsumerAuthEndpointFilter.ConsumerIdItemKey, out var idValue) ? (Guid?)idValue : null;
    var consumerName = httpContext.Items.TryGetValue(ConsumerAuthEndpointFilter.ConsumerNameItemKey, out var nameValue)
        ? (string?)nameValue
        : null;
    var allowedModelIds = httpContext.Items.TryGetValue(ConsumerAuthEndpointFilter.AllowedModelIdsItemKey, out var allowedValue)
        ? (IReadOnlySet<Guid>?)allowedValue
        : null;

    var failure = await router.RouteAsync(
        httpContext, operationType, cancellationToken, consumerId, consumerName, allowedModelIds);
    if (failure is not null)
    {
        await WriteRoutingFailureAsync(httpContext, failure);
    }
}

static async Task WriteRoutingFailureAsync(HttpContext httpContext, RoutingFailure failure)
{
    if (failure.UpstreamFailure is not null)
    {
        // Surface the first endpoint's own error (status/headers/body) verbatim instead of a generic 503 —
        // e.g. a 429's Retry-After/rate-limit headers tell the client how long to back off (architecture.md §9).
        await failure.UpstreamFailure.WriteToAsync(httpContext, httpContext.RequestAborted);
        return;
    }

    httpContext.Response.StatusCode = failure.Reason switch
    {
        RoutingFailureReason.ModelNotFound => StatusCodes.Status404NotFound,
        RoutingFailureReason.ModelAccessDenied => StatusCodes.Status403Forbidden,
        RoutingFailureReason.NoHealthyEndpoint => StatusCodes.Status503ServiceUnavailable,
        RoutingFailureReason.StickyEndpointUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };
    await httpContext.Response.WriteAsJsonAsync(new { error = failure.Message });
}

static bool IsSafeReturnUrl(string? returnUrl) =>
    !string.IsNullOrWhiteSpace(returnUrl) &&
    returnUrl.StartsWith('/') &&
    !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
    !returnUrl.Contains('\\');

/// <summary>A short-lived credential for one Realtime WebSocket handshake.</summary>
sealed record RealtimeTicketResponse(string Ticket, int ExpiresInSeconds);

/// <summary>Probes Postgres/Vault reachability via the registered ports (architecture.md §12).</summary>
static class HealthChecks
{
    public static async Task<IResult> CheckReadyAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var checks = new Dictionary<string, string>
        {
            ["vault"] = await ProbeAsync(() =>
                services.GetRequiredService<ISecretResolver>().TryResolveAsync("__health_check__", cancellationToken)),
            ["postgres"] = await ProbeAsync(() => services.GetRequiredService<IModelRepository>().GetAllAsync(cancellationToken)),
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

public sealed record UpdateModelBody(
    RoutingStrategy Strategy,
    IReadOnlyCollection<ModelCapability>? Capabilities = null);

public sealed record AddEndpointBody(
    Guid EndpointId,
    Guid ApiRouteId,
    int Priority,
    bool Enabled = true);

public sealed record UpdateEndpointBody(
    int? Priority = null,
    bool? Enabled = null);

public sealed record UpdateEndpointConfigurationBody(
    string Name,
    string BaseUrl,
    string? ManagedIdentityScope,
    int TimeoutSeconds,
    int MaxRetries,
    string? ManagedIdentityTenantId = null);

public sealed record SetEndpointSecretBody(string ApiKeyPlaintext);

public sealed record ApiRouteBody(string Name, Provider Provider, string PathPrefix);

public sealed record ConsumerResponse(
    Guid Id,
    string Name,
    string AuthenticationType,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    bool HasApiKey1,
    bool HasApiKey2,
    IReadOnlyList<Guid> AllowedModelIds)
{
    public static ConsumerResponse From(Consumer consumer) => new(
        consumer.Id,
        consumer.Name,
        consumer.IsOidc ? "oidc" : "apiKey",
        consumer.Enabled,
        consumer.CreatedAt,
        consumer.RevokedAt,
        consumer.ApiKeyHash is not null,
        consumer.ApiKeyHash2 is not null,
        consumer.AllowedModelIds);
}

/// <summary>Exposes the top-level program to <c>WebApplicationFactory&lt;Program&gt;</c> in tests.</summary>
public partial class Program;
