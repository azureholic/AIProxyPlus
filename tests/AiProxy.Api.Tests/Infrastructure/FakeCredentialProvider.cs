using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;

namespace AiProxy.Api.Tests.Infrastructure;

/// <summary>Fake <see cref="ICredentialProvider"/> — WireMock doesn't validate auth, so any fixed header works.</summary>
public sealed class FakeCredentialProvider : ICredentialProvider
{
    public Task<EndpointCredential> GetCredentialAsync(ModelEndpoint assignment, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EndpointCredential("Authorization", "Bearer test-token"));
}
