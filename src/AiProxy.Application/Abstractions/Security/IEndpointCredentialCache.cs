namespace AiProxy.Application.Abstractions;

/// <summary>Invalidates cached endpoint credentials after their secret-store value changes.</summary>
public interface IEndpointCredentialCache
{
    void Invalidate(string secretRef);
}