using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Infrastructure.Secrets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AiProxy.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the real <c>Program</c> (real routing/forwarding/failover/affinity logic) but swaps the
/// Postgres/Vault-backed ports for fast in-memory fakes, so these tests only depend on WireMock.Net fake
/// backends — no Docker required.
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    private const string TestPepper = "test-pepper-not-for-production-0123456789";

    public const string AdminApiKey = "test-admin-key";
    public const string DefaultConsumerApiKey = "test-consumer-key";
    public const string WorkloadAudience = "api://ai-proxy-tests";
    public const string WorkloadOidcIssuer = "https://workload-idp.test";
    public const string ManagedIdentityIssuer = "https://login.microsoftonline.com/test-tenant/v2.0";

    public static SymmetricSecurityKey WorkloadSigningKey { get; } =
        new(Convert.FromBase64String("qJfZKpI3GJ6Y9Q04GghV7Jiwm5QFVYC7adB6NfKgk7I=")) { KeyId = "test-workload-key" };

    public FakeModelRepository Models { get; } = new();

    public FakeEndpointRepository Endpoints { get; }

    public FakeApiRouteRepository ApiRoutes { get; }

    public FakeSessionAffinityStore Affinity { get; } = new();

    public FakeUsageRecordRepository UsageRecords { get; } = new();

    public FakeConsumerRepository Consumers { get; } = new();

    public FakeRealtimeTicketService RealtimeTickets { get; } = new();

    public FakeSecretResolver Secrets { get; } = new();

    public FakeCredentialProvider Credentials { get; } = new();

    public Consumer DefaultConsumer { get; }

    public ApiTestFactory()
    {
        Endpoints = new FakeEndpointRepository(Models);
        ApiRoutes = new FakeApiRouteRepository(Models);
        Secrets.Seed("consumer-key-pepper", TestPepper);
        Secrets.Seed("admin-api-key-hash", AdminApiKeyAuthenticator.Hash(AdminApiKey));

        var hasher = new ApiKeyHasher(TestPepper);
        DefaultConsumer = Consumer.Create(Guid.NewGuid(), "default-test-consumer", hasher.Hash(DefaultConsumerApiKey));
        Consumers.Seed(DefaultConsumer);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development so /openapi/v1.json (and the Scalar UI) are mapped for OpenApiDocument_IsGeneratedSuccessfully.
        builder.UseEnvironment("Development");
        builder.UseSetting("Authentication:AdminApiKeyFallbackEnabled", "true");
        builder.UseSetting("Database:ApplyMigrations", "false");
        builder.UseSetting("Authentication:Workload:Oidc:Enabled", "true");
        builder.UseSetting("Authentication:Workload:Oidc:Authority", WorkloadOidcIssuer);
        builder.UseSetting("Authentication:Workload:Oidc:Audience", WorkloadAudience);
        builder.UseSetting("Authentication:Workload:Oidc:RequiredScope", "aiproxy.invoke");
        builder.UseSetting("Authentication:Workload:Oidc:AllowedSigningAlgorithms:0", SecurityAlgorithms.HmacSha256);
        builder.UseSetting("Authentication:Workload:ManagedIdentity:Enabled", "true");
        builder.UseSetting("Authentication:Workload:ManagedIdentity:Authority", ManagedIdentityIssuer);
        builder.UseSetting("Authentication:Workload:ManagedIdentity:Audience", WorkloadAudience);
        builder.UseSetting("Authentication:Workload:ManagedIdentity:RequiredRole", "AiProxy.Invoke");
        builder.UseSetting(
            "Authentication:Workload:ManagedIdentity:AllowedSigningAlgorithms:0",
            SecurityAlgorithms.HmacSha256);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IModelRepository>(Models);
            services.AddSingleton<IEndpointRepository>(Endpoints);
            services.AddSingleton<IApiRouteRepository>(ApiRoutes);
            services.AddSingleton<ISessionAffinityStore>(Affinity);
            services.AddSingleton<ICredentialProvider>(Credentials);
            services.AddSingleton<IUsageRecordRepository>(UsageRecords);
            services.AddSingleton<IConsumerRepository>(Consumers);
            services.AddSingleton<IRealtimeTicketService>(RealtimeTickets);
            services.AddSingleton<ISecretResolver>(Secrets);
            services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>>(new TestJwtBearerPostConfigure());
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        // All ingress routes now require consumer auth (Phase 7) — every test client is pre-authenticated as
        // the seeded default consumer so existing/new tests don't each need to attach the header themselves.
        // A request that sets its own X-Api-Key takes precedence over this default.
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", DefaultConsumerApiKey);
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", AdminApiKey);
    }

    private sealed class TestJwtBearerPostConfigure : IPostConfigureOptions<JwtBearerOptions>
    {
        public void PostConfigure(string? name, JwtBearerOptions options)
        {
            var issuer = name switch
            {
                "WorkloadOidc" => WorkloadOidcIssuer,
                "ManagedIdentity" => ManagedIdentityIssuer,
                _ => null,
            };
            if (issuer is null)
            {
                return;
            }

            options.Configuration = new OpenIdConnectConfiguration
            {
                Issuer = issuer,
                SigningKeys = { WorkloadSigningKey },
            };
            options.TokenValidationParameters.ValidIssuer = issuer;
            options.TokenValidationParameters.IssuerSigningKey = WorkloadSigningKey;
        }
    }
}
