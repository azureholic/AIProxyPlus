using System.Collections.Concurrent;
using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;

namespace AiProxy.Api.Tests.Infrastructure;

/// <summary>In-memory <see cref="IModelDeploymentRepository"/> fake for fast, Docker-free API tests.</summary>
public sealed class FakeModelDeploymentRepository : IModelDeploymentRepository
{
    private readonly ConcurrentDictionary<string, ModelDeployment> _byName = new();

    public void Seed(ModelDeployment deployment) => _byName[deployment.Name] = deployment;

    public Task<ModelDeployment?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byName.GetValueOrDefault(name));

    public Task<IReadOnlyList<ModelDeployment>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModelDeployment>>(_byName.Values.ToList());

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byName.ContainsKey(name));

    public Task AddAsync(ModelDeployment modelDeployment, CancellationToken cancellationToken = default)
    {
        _byName[modelDeployment.Name] = modelDeployment;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ModelDeployment modelDeployment, CancellationToken cancellationToken = default)
    {
        _byName[modelDeployment.Name] = modelDeployment;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid modelDeploymentId, CancellationToken cancellationToken = default)
    {
        var match = _byName.Values.FirstOrDefault(d => d.Id == modelDeploymentId);
        if (match is not null)
        {
            _byName.TryRemove(match.Name, out _);
        }

        return Task.CompletedTask;
    }
}
