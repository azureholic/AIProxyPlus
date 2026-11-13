var builder = DistributedApplication.CreateBuilder(args);

var postgresServer = builder.AddAzurePostgresFlexibleServer("postgres")
    .WithPasswordAuthentication(
        builder.ExecutionContext.IsPublishMode ||
        !string.IsNullOrWhiteSpace(builder.Configuration["Parameters:postgres-username"])
            ? builder.AddParameter("postgres-username")
            : null,
        builder.ExecutionContext.IsPublishMode ||
        !string.IsNullOrWhiteSpace(builder.Configuration["Parameters:postgres-password"])
            ? builder.AddParameter("postgres-password", secret: true)
            : null);
var postgres = postgresServer.RunAsContainer(container => container
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent))
    .AddDatabase("aiproxyplusdb");

if (builder.ExecutionContext.IsPublishMode)
{
    builder.AddAzureContainerAppEnvironment("environment");
    var entraAuthority = builder.AddParameter("entra-authority");
    var entraClientId = builder.AddParameter("entra-client-id");
    var apiIdentityClientId = builder.AddParameter("api-identity-client-id");
    var applicationInsightsConnectionString = builder.AddParameter("applicationinsights-connection-string");
    var keyVault = builder.AddAzureKeyVault("keyvault");
    var entraClientSecret = keyVault.GetSecret("oidc-client-secret");

#pragma warning disable ASPIREPROBES001
    var api = builder.AddProject<Projects.AIProxyPlus_Api>("api")
        .WithReference(postgres)
        .WithReference(keyVault)
        .WithRoleAssignments(keyVault, Azure.Provisioning.KeyVault.KeyVaultBuiltInRole.KeyVaultSecretsOfficer)
        .WithEnvironment("Secrets__Provider", "AzureKeyVault")
        .WithEnvironment("AzureKeyVault__UseManagedIdentity", "true")
        .WithEnvironment("AzureKeyVault__ManagedIdentityClientId", apiIdentityClientId)
        .WithEnvironment("Logging__File__Enabled", "false")
        .WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", applicationInsightsConnectionString)
        .WithEnvironment("Authentication__Oidc__Enabled", "true")
        .WithEnvironment("Authentication__Oidc__DisplayName", "Microsoft Entra ID")
        .WithEnvironment("Authentication__Oidc__Authority", entraAuthority)
        .WithEnvironment("Authentication__Oidc__ClientId", entraClientId)
        .WithEnvironment("Authentication__Oidc__ClientSecret", entraClientSecret)
        .WithEnvironment("Authentication__Oidc__Scopes__0", "openid")
        .WithEnvironment("Authentication__Oidc__Scopes__1", "profile")
        .WithEnvironment("Authentication__Oidc__UsernameClaim", "preferred_username")
        .WithEnvironment("Authentication__Oidc__GetClaimsFromUserInfo", "false")
        .WithEnvironment("Authentication__Oidc__CallbackPath", "/signin-oidc")
        .WithEnvironment("Authentication__Oidc__SignedOutCallbackPath", "/signout-callback-oidc")
        .WithEnvironment("Authentication__Oidc__SessionLifetimeMinutes", "480")
        .WithEnvironment("Authentication__Oidc__AllowedSigningAlgorithms__0", "RS256")
        .WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true")
        .WithHttpEndpoint(targetPort: 8080)
        .WithHttpProbe(ProbeType.Liveness, "/health/live", initialDelaySeconds: 10, periodSeconds: 10, timeoutSeconds: 5, failureThreshold: 3)
        .WithHttpProbe(ProbeType.Readiness, "/health/ready", initialDelaySeconds: 10, periodSeconds: 10, timeoutSeconds: 5, failureThreshold: 3)
        .WithExternalHttpEndpoints()
        .PublishAsAzureContainerApp((_, app) =>
        {
            app.Template.Scale.MinReplicas = 1;
            app.Template.Scale.MaxReplicas = 3;
        })
        .PublishAsDockerFile(container => container.WithDockerfile(
            contextPath: "../..",
            dockerfilePath: "src/AIProxyPlus.Api/Dockerfile"))
        .WaitFor(postgres)
        .WaitFor(keyVault);
    // WithHttpProbe adds a duplicate health-check key when startup and liveness share a path.
    api.WithAnnotation(new EndpointProbeAnnotation
    {
        Type = ProbeType.Startup,
        EndpointReference = api.GetEndpoint("http"),
        Path = "/health/live",
        PeriodSeconds = 10,
        TimeoutSeconds = 5,
        FailureThreshold = 60,
    });
#pragma warning restore ASPIREPROBES001
}
else
{
    var oidcSection = builder.Configuration.GetSection("Authentication:Oidc");
    var oidc = new OidcAppHostOptions
    {
        Enabled = bool.TryParse(oidcSection["Enabled"], out var enabled) && enabled,
        DisplayName = oidcSection["DisplayName"] ?? "OpenID Connect",
        Authority = oidcSection["Authority"] ?? string.Empty,
        ClientId = oidcSection["ClientId"] ?? string.Empty,
        ClientSecretParameter = oidcSection["ClientSecretParameter"] ?? string.Empty,
        Scopes = oidcSection.GetSection("Scopes").GetChildren().Select(scope => scope.Value!).ToArray(),
        UsernameClaim = oidcSection["UsernameClaim"] ?? "preferred_username",
        GetClaimsFromUserInfo = bool.TryParse(oidcSection["GetClaimsFromUserInfo"], out var getUserInfo) && getUserInfo,
        CallbackPath = oidcSection["CallbackPath"] ?? "/signin-oidc",
        SignedOutCallbackPath = oidcSection["SignedOutCallbackPath"] ?? "/signout-callback-oidc",
        SessionLifetimeMinutes = int.TryParse(oidcSection["SessionLifetimeMinutes"], out var sessionMinutes)
            ? sessionMinutes
            : 480,
        AllowedSigningAlgorithms = oidcSection.GetSection("AllowedSigningAlgorithms")
            .GetChildren().Select(algorithm => algorithm.Value!).ToArray(),
    };
    oidc.Validate();

    var oidcClientSecret = builder.AddParameter(oidc.ClientSecretParameter, secret: true);
    var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);
    var proxyAdminPassword = builder.AddParameter("proxy-admin-password", secret: true);

    var vault = builder.AddContainer("vault", "hashicorp/vault", "2.0.3")
        .WithEntrypoint("/bin/sh")
        .WithArgs("/vault/config/bootstrap.sh")
        .WithHttpEndpoint(targetPort: 8200, name: "http")
        .WithHttpHealthCheck("/v1/sys/health", endpointName: "http")
        .WithBindMount("./vault", "/vault/config", isReadOnly: true)
        .WithVolume("vault-data", "/vault/file")
        .WithLifetime(ContainerLifetime.Persistent);

    var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.3.1")
        .WithArgs("start-dev", "--import-realm", "--health-enabled=true")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakAdminPassword)
        .WithEnvironment("AIPROXYPLUS_OIDC_CLIENT_SECRET", oidcClientSecret)
        .WithEnvironment("AIPROXYPLUS_ADMIN_PASSWORD", proxyAdminPassword)
        .WithBindMount("./keycloak", "/opt/keycloak/data/import", isReadOnly: true)
        .WithVolume("keycloak-data", "/opt/keycloak/data")
        .WithHttpEndpoint(port: 8081, targetPort: 8080, name: "http")
        .WithHttpEndpoint(targetPort: 9000, name: "management")
        .WithHttpHealthCheck("/health/ready", endpointName: "management")
        .WithLifetime(ContainerLifetime.Persistent);

    var api = builder.AddProject<Projects.AIProxyPlus_Api>("api")
        .WithReference(postgres)
        .WithEnvironment("Secrets__Provider", "HashiCorpVault")
        .WithEnvironment("Vault__Address", vault.GetEndpoint("http"))
        .WithEnvironment("Vault__Token", "dev-only-root-token")
        .WithEnvironment("Authentication__Oidc__Enabled", oidc.Enabled.ToString())
        .WithEnvironment("Authentication__Oidc__DisplayName", oidc.DisplayName)
        .WithEnvironment("Authentication__Oidc__Authority", oidc.Authority)
        .WithEnvironment("Authentication__Oidc__ClientId", oidc.ClientId)
        .WithEnvironment("Authentication__Oidc__ClientSecret", oidcClientSecret)
        .WithEnvironment("Authentication__Oidc__UsernameClaim", oidc.UsernameClaim)
        .WithEnvironment("Authentication__Oidc__GetClaimsFromUserInfo", oidc.GetClaimsFromUserInfo.ToString())
        .WithEnvironment("Authentication__Oidc__CallbackPath", oidc.CallbackPath)
        .WithEnvironment("Authentication__Oidc__SignedOutCallbackPath", oidc.SignedOutCallbackPath)
        .WithEnvironment("Authentication__Oidc__SessionLifetimeMinutes", oidc.SessionLifetimeMinutes.ToString())
        .WaitFor(postgres)
        .WaitFor(vault)
        .WaitFor(keycloak);

    for (var index = 0; index < oidc.Scopes.Length; index++)
    {
        api.WithEnvironment($"Authentication__Oidc__Scopes__{index}", oidc.Scopes[index]);
    }

    for (var index = 0; index < oidc.AllowedSigningAlgorithms.Length; index++)
    {
        api.WithEnvironment(
            $"Authentication__Oidc__AllowedSigningAlgorithms__{index}",
            oidc.AllowedSigningAlgorithms[index]);
    }
}

builder.Build().Run();

sealed class OidcAppHostOptions
{
    public bool Enabled { get; init; }
    public string DisplayName { get; init; } = "OpenID Connect";
    public string Authority { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecretParameter { get; init; } = string.Empty;
    public string[] Scopes { get; init; } = ["openid", "profile"];
    public string UsernameClaim { get; init; } = "preferred_username";
    public bool GetClaimsFromUserInfo { get; init; }
    public string CallbackPath { get; init; } = "/signin-oidc";
    public string SignedOutCallbackPath { get; init; } = "/signout-callback-oidc";
    public int SessionLifetimeMinutes { get; init; } = 480;
    public string[] AllowedSigningAlgorithms { get; init; } = ["RS256"];

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var authority) ||
            (authority.Scheme != Uri.UriSchemeHttps && !authority.IsLoopback))
        {
            throw new InvalidOperationException("Authentication:Oidc:Authority must be an absolute HTTPS or loopback URI.");
        }

        if (string.IsNullOrWhiteSpace(DisplayName) || string.IsNullOrWhiteSpace(ClientId) ||
            string.IsNullOrWhiteSpace(ClientSecretParameter) ||
            string.IsNullOrWhiteSpace(UsernameClaim) || !Scopes.Contains("openid", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Authentication:Oidc requires DisplayName, ClientId, ClientSecretParameter, UsernameClaim, and the openid scope.");
        }
    }
}
