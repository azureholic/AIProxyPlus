using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AiProxy.Infrastructure.Tests.Persistence;

[Collection(nameof(PostgresCollection))]
public class ModelRepositoryTests(PostgresContainerFixture fixture)
{
    private static Model NewModel(string? name = null) =>
        Model.Create(Guid.NewGuid(), name ?? $"gpt-4o-{Guid.NewGuid():N}");

    private static ModelEndpoint NewEndpoint(Guid modelId, int priority = 1) =>
        ModelEndpoint.Create(
            modelId,
            Endpoint.Create(Guid.NewGuid(), $"OpenAI {Guid.NewGuid():N}", "https://api.openai.com",
                ApiFamily.OpenAI, AuthMode.ApiKey, secretRef: $"endpoints/{Guid.NewGuid()}"),
            priority);

    [Fact]
    public async Task AddAsync_Then_GetByNameAsync_RoundTripsModelWithEndpoints()
    {
        await using var writeContext = fixture.CreateDbContext();
        var repository = new ModelRepository(writeContext);

        var model = NewModel();
        model.AddEndpoint(NewEndpoint(model.Id, priority: 1));
        model.AddEndpoint(NewEndpoint(model.Id, priority: 2));
        await repository.AddAsync(model);

        // Fresh context/repository so we know this is reading from Postgres, not the change tracker.
        await using var readContext = fixture.CreateDbContext();
        var readRepository = new ModelRepository(readContext);
        var loaded = await readRepository.GetByNameAsync(model.Name);

        loaded.Should().NotBeNull();
        loaded!.Endpoints.Should().HaveCount(2);
        loaded.Endpoints.Select(e => e.Priority).Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public async Task GetByNameAsync_ForUnknownName_ReturnsNull()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repository = new ModelRepository(dbContext);

        var result = await repository.GetByNameAsync($"missing-{Guid.NewGuid():N}");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsByNameAsync_AfterAdd_ReturnsTrue()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repository = new ModelRepository(dbContext);
        var model = NewModel();
        await repository.AddAsync(model);

        (await repository.ExistsByNameAsync(model.Name)).Should().BeTrue();
        (await repository.ExistsByNameAsync($"missing-{Guid.NewGuid():N}")).Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_WithDuplicateName_ThrowsDueToUniqueIndex()
    {
        var name = $"gpt-4o-{Guid.NewGuid():N}";

        await using var context1 = fixture.CreateDbContext();
        await new ModelRepository(context1).AddAsync(NewModel(name));

        await using var context2 = fixture.CreateDbContext();
        var act = () => new ModelRepository(context2).AddAsync(NewModel(name));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UpdateAsync_PersistsAddedEndpointAndPolicyChanges()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";

        await using (var writeContext = fixture.CreateDbContext())
        {
            var model = NewModel(modelName);
            model.AddEndpoint(NewEndpoint(model.Id, priority: 1));
            await new ModelRepository(writeContext).AddAsync(model);
        }

        // Simulate a realistic usage pattern: the update happens in a fresh scope/DbContext that
        // re-fetches the aggregate, rather than reusing the same tracked instance across two calls.
        await using (var updateContext = fixture.CreateDbContext())
        {
            var updateRepository = new ModelRepository(updateContext);
            var reloaded = await updateRepository.GetByNameAsync(modelName);
            reloaded!.SetStrategy(RoutingStrategy.Priority);
            reloaded.AddEndpoint(NewEndpoint(reloaded.Id, priority: 2));
            await updateRepository.UpdateAsync(reloaded);
        }

        await using var readContext = fixture.CreateDbContext();
        var loaded = await new ModelRepository(readContext).GetByNameAsync(modelName);

        loaded!.Strategy.Should().Be(RoutingStrategy.Priority);
        loaded.Endpoints.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteAsync_CascadesAssignmentsAndPreservesReusableEndpoint()
    {
        await using var writeContext = fixture.CreateDbContext();
        var repository = new ModelRepository(writeContext);
        var model = NewModel();
        var endpoint = NewEndpoint(model.Id, priority: 1);
        model.AddEndpoint(endpoint);
        await repository.AddAsync(model);

        await repository.DeleteAsync(model.Id);

        await using var readContext = fixture.CreateDbContext();
        (await new ModelRepository(readContext).GetByNameAsync(model.Name)).Should().BeNull();
        (await readContext.Set<Endpoint>().AnyAsync(e => e.Id == endpoint.Id)).Should().BeTrue();
        (await readContext.Set<ModelEndpoint>().AnyAsync(e => e.EndpointId == endpoint.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ForUnknownId_DoesNotThrow()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repository = new ModelRepository(dbContext);

        var act = () => repository.DeleteAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }
}
