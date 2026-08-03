using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;

namespace AiProxy.Domain.Entities;

/// <summary>A reusable provider API surface exposed by one or more endpoints.</summary>
public sealed class ApiRoute
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public Provider Provider { get; private set; }
    public string PathPrefix { get; private set; }
    public bool IsBuiltIn { get; private set; }

    private ApiRoute()
    {
        Name = null!;
        PathPrefix = null!;
    }

    private ApiRoute(Guid id, string name, Provider provider, string pathPrefix, bool isBuiltIn)
    {
        Id = id;
        Name = name;
        Provider = provider;
        PathPrefix = pathPrefix;
        IsBuiltIn = isBuiltIn;
    }

    public static ApiRoute Create(
        Guid id,
        string name,
        Provider provider,
        string pathPrefix,
        bool isBuiltIn = false)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("ApiRoute.Id must be valid.");
        }

        return new ApiRoute(id, NormalizeName(name), provider, NormalizePathPrefix(pathPrefix), isBuiltIn);
    }

    public void Update(string name, Provider provider, string pathPrefix)
    {
        Name = NormalizeName(name);
        Provider = provider;
        PathPrefix = NormalizePathPrefix(pathPrefix);
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("ApiRoute.Name must not be empty.");
        }

        return name.Trim();
    }

    private static string NormalizePathPrefix(string pathPrefix)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix))
        {
            throw new DomainException("ApiRoute.PathPrefix must not be empty.");
        }

        var trimmed = pathPrefix.Trim();
        if (trimmed.Contains(':') || trimmed.Contains('?') || trimmed.Contains('#') || trimmed.Contains('\\'))
        {
            throw new DomainException("ApiRoute.PathPrefix must contain only an absolute path component.");
        }

        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? "/" : $"/{string.Join('/', segments)}";
    }
}