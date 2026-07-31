var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("aiproxydb");

// Dev-mode Vault (architecture.md §8): well-known root token, mlock disabled since dev containers typically
// lack IPC_LOCK — acceptable for local Aspire runs only, never for real deployments.
var vault = builder.AddContainer("vault", "hashicorp/vault")
    .WithEnvironment("VAULT_DEV_ROOT_TOKEN_ID", "dev-only-root-token")
    .WithEnvironment("VAULT_DEV_LISTEN_ADDRESS", "0.0.0.0:8200")
    .WithEnvironment("VAULT_DISABLE_MLOCK", "true")
    .WithHttpEndpoint(targetPort: 8200, name: "http")
    .WithVolume("vault-data", "/vault/file");

var api = builder.AddProject<Projects.AiProxy_Api>("api")
    .WithReference(postgres)
    .WithEnvironment("Vault__Address", vault.GetEndpoint("http"))
    .WithEnvironment("Vault__Token", "dev-only-root-token")
    .WaitFor(postgres)
    .WaitFor(vault);

builder.Build().Run();
