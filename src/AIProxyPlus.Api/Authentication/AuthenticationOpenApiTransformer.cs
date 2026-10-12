using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AIProxyPlus.Api.Authentication;

public sealed class AuthenticationOpenApiTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["BrowserSession"] = ApiKeyScheme("__Host-AIProxyPlus.Session", ParameterLocation.Cookie,
                "Encrypted OIDC BFF session. Unsafe requests also require X-CSRF-TOKEN."),
            ["AdminApiKey"] = ApiKeyScheme("X-Admin-Api-Key", ParameterLocation.Header,
                "Optional migration or break-glass administrator key."),
            ["ConsumerApiKey"] = ApiKeyScheme("X-Api-Key", ParameterLocation.Header,
                "Consumer API key for non-browser inference clients."),
            ["WorkloadBearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "OIDC or Microsoft Entra access token issued for AIProxyPlus.",
            },
            ["RealtimeTicket"] = ApiKeyScheme("ticket", ParameterLocation.Query,
                "Short-lived, single-use ticket issued by POST /api/v1/auth/realtime-ticket."),
            ["LegacyRealtimeApiKey"] = ApiKeyScheme("api_key", ParameterLocation.Query,
                "Legacy Consumer API key query fallback for non-browser Realtime clients."),
        };

        if (document.Paths is null)
        {
            return Task.CompletedTask;
        }

        foreach (var (path, pathItem) in document.Paths)
        {
            if (pathItem.Operations is null)
            {
                continue;
            }

            foreach (var (method, operation) in pathItem.Operations)
            {
                var schemes = GetSchemes(path, method.Method);
                if (schemes.Length == 0)
                {
                    continue;
                }

                operation.Security = schemes
                    .Select(scheme => new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(scheme, document)] = [],
                    })
                    .ToList();

                if (schemeUsesBrowserSession(schemes) && method.Method is not ("GET" or "HEAD" or "OPTIONS"))
                {
                    operation.Description = string.Join(
                        " ",
                        operation.Description,
                        "Cookie-authenticated requests must include the X-CSRF-TOKEN antiforgery header.").Trim();
                }
            }
        }

        return Task.CompletedTask;
    }

    private static OpenApiSecurityScheme ApiKeyScheme(string name, ParameterLocation location, string description) =>
        new()
        {
            Type = SecuritySchemeType.ApiKey,
            Name = name,
            In = location,
            Description = description,
        };

    private static string[] GetSchemes(string path, string method)
    {
        if (path.StartsWith("/api/v1/config", StringComparison.Ordinal) ||
            path.StartsWith("/api/v1/usage", StringComparison.Ordinal))
        {
            return ["BrowserSession", "AdminApiKey"];
        }

        if (path == "/openai/v1/realtime")
        {
            return ["ConsumerApiKey", "RealtimeTicket", "LegacyRealtimeApiKey"];
        }

        if (path.StartsWith("/openai/", StringComparison.Ordinal) ||
            path.StartsWith("/anthropic/", StringComparison.Ordinal))
        {
            return ["BrowserSession", "ConsumerApiKey", "WorkloadBearer"];
        }

        if (path == "/api/v1/auth/realtime-ticket" || path == "/api/v1/auth/logout")
        {
            return ["BrowserSession"];
        }

        return [];
    }

    private static bool schemeUsesBrowserSession(string[] schemes) => schemes.Contains("BrowserSession", StringComparer.Ordinal);
}