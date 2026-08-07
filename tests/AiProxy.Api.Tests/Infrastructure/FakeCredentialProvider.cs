using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;

namespace AiProxy.Api.Tests.Infrastructure;

/// <summary>Fake <see cref="ICredentialProvider"/> — WireMock doesn't validate auth, so any fixed header works.</summary>
public sealed class FakeCredentialProvider : ICredentialProvider
{
    public EndpointCredentialCandidates Candidates { get; set; } =
        new(new EndpointCredential("Authorization", "Bearer test-token"), null);

    public Task<EndpointCredential> GetCredentialAsync(ModelEndpoint assignment, CancellationToken cancellationToken = default) =>
        Task.FromResult(Candidates.Active);

    public Task<EndpointCredentialCandidates> GetCredentialCandidatesAsync(
        ModelEndpoint assignment,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Candidates);
}
