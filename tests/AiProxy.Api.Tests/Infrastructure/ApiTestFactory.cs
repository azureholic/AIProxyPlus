using AiProxy.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AiProxy.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the real <c>Program</c> (real routing/forwarding/failover/affinity logic) but swaps the
/// Postgres/Vault-backed ports for fast in-memory fakes, so these tests only depend on WireMock.Net fake
/// backends — no Docker required.
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    public FakeModelDeploymentRepository Deployments { get; } = new();

    public FakeSessionAffinityStore Affinity { get; } = new();

    public FakeUsageRecordRepository UsageRecords { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IModelDeploymentRepository>(Deployments);
            services.AddSingleton<ISessionAffinityStore>(Affinity);
            services.AddSingleton<ICredentialProvider, FakeCredentialProvider>();
            services.AddSingleton<IUsageRecordRepository>(UsageRecords);
        });
    }
}
