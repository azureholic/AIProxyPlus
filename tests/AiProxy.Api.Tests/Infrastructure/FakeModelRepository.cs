using System.Collections.Concurrent;
using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;

namespace AiProxy.Api.Tests.Infrastructure;

/// <summary>In-memory <see cref="IModelRepository"/> fake for fast, Docker-free API tests.</summary>
public sealed class FakeModelRepository : IModelRepository
{
    private readonly ConcurrentDictionary<string, Model> _byName = new();

    public void Seed(Model model) => _byName[model.Name] = model;

    public Task<Model?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byName.GetValueOrDefault(name));

    public Task<IReadOnlyList<Model>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Model>>(_byName.Values.ToList());

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byName.ContainsKey(name));

    public Task AddAsync(Model model, CancellationToken cancellationToken = default)
    {
        _byName[model.Name] = model;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Model model, CancellationToken cancellationToken = default)
    {
        _byName[model.Name] = model;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid modelId, CancellationToken cancellationToken = default)
    {
        var match = _byName.Values.FirstOrDefault(d => d.Id == modelId);
        if (match is not null)
        {
            _byName.TryRemove(match.Name, out _);
        }

        return Task.CompletedTask;
    }
}
