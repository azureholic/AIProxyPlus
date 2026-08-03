using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AiProxy.Infrastructure.Tests.Persistence;

[Collection(nameof(PostgresCollection))]
public class ModelDeploymentRepositoryTests(PostgresContainerFixture fixture)
{
    private static ModelDeployment NewDeployment(string? name = null) =>
        ModelDeployment.Create(Guid.NewGuid(), name ?? $"gpt-4o-{Guid.NewGuid():N}");

    private static ModelDeploymentEndpoint NewEndpoint(Guid modelDeploymentId, int priority = 1) =>
        ModelDeploymentEndpoint.Create(
            modelDeploymentId,
            Endpoint.Create(Guid.NewGuid(), $"OpenAI {Guid.NewGuid():N}", "https://api.openai.com",
                ApiFamily.OpenAI, AuthMode.ApiKey, secretRef: $"endpoints/{Guid.NewGuid()}"),
            priority);

    [Fact]
    public async Task AddAsync_Then_GetByNameAsync_RoundTripsDeploymentWithEndpoints()
    {
        await using var writeContext = fixture.CreateDbContext();
        var repository = new ModelDeploymentRepository(writeContext);

        var deployment = NewDeployment();
        deployment.AddEndpoint(NewEndpoint(deployment.Id, priority: 1));
        deployment.AddEndpoint(NewEndpoint(deployment.Id, priority: 2));
        await repository.AddAsync(deployment);

        // Fresh context/repository so we know this is reading from Postgres, not the change tracker.
        await using var readContext = fixture.CreateDbContext();
        var readRepository = new ModelDeploymentRepository(readContext);
        var loaded = await readRepository.GetByNameAsync(deployment.Name);

        loaded.Should().NotBeNull();
        loaded!.Endpoints.Should().HaveCount(2);
        loaded.Endpoints.Select(e => e.Priority).Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public async Task GetByNameAsync_ForUnknownName_ReturnsNull()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repository = new ModelDeploymentRepository(dbContext);

        var result = await repository.GetByNameAsync($"missing-{Guid.NewGuid():N}");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsByNameAsync_AfterAdd_ReturnsTrue()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repository = new ModelDeploymentRepository(dbContext);
        var deployment = NewDeployment();
        await repository.AddAsync(deployment);

        (await repository.ExistsByNameAsync(deployment.Name)).Should().BeTrue();
        (await repository.ExistsByNameAsync($"missing-{Guid.NewGuid():N}")).Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_WithDuplicateName_ThrowsDueToUniqueIndex()
    {
        var name = $"gpt-4o-{Guid.NewGuid():N}";

        await using var context1 = fixture.CreateDbContext();
        await new ModelDeploymentRepository(context1).AddAsync(NewDeployment(name));

        await using var context2 = fixture.CreateDbContext();
        var act = () => new ModelDeploymentRepository(context2).AddAsync(NewDeployment(name));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UpdateAsync_PersistsAddedEndpointAndPolicyChanges()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";

        await using (var writeContext = fixture.CreateDbContext())
        {
            var deployment = NewDeployment(deploymentName);
            deployment.AddEndpoint(NewEndpoint(deployment.Id, priority: 1));
            await new ModelDeploymentRepository(writeContext).AddAsync(deployment);
        }

        // Simulate a realistic usage pattern: the update happens in a fresh scope/DbContext that
        // re-fetches the aggregate, rather than reusing the same tracked instance across two calls.
        await using (var updateContext = fixture.CreateDbContext())
        {
            var updateRepository = new ModelDeploymentRepository(updateContext);
            var reloaded = await updateRepository.GetByNameAsync(deploymentName);
            reloaded!.SetStrategy(RoutingStrategy.Priority);
            reloaded.AddEndpoint(NewEndpoint(reloaded.Id, priority: 2));
            await updateRepository.UpdateAsync(reloaded);
        }

        await using var readContext = fixture.CreateDbContext();
        var loaded = await new ModelDeploymentRepository(readContext).GetByNameAsync(deploymentName);

        loaded!.Strategy.Should().Be(RoutingStrategy.Priority);
        loaded.Endpoints.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteAsync_CascadesAssignmentsAndPreservesReusableEndpoint()
    {
        await using var writeContext = fixture.CreateDbContext();
        var repository = new ModelDeploymentRepository(writeContext);
        var deployment = NewDeployment();
        var endpoint = NewEndpoint(deployment.Id, priority: 1);
        deployment.AddEndpoint(endpoint);
        await repository.AddAsync(deployment);

        await repository.DeleteAsync(deployment.Id);

        await using var readContext = fixture.CreateDbContext();
        (await new ModelDeploymentRepository(readContext).GetByNameAsync(deployment.Name)).Should().BeNull();
        (await readContext.Set<Endpoint>().AnyAsync(e => e.Id == endpoint.Id)).Should().BeTrue();
        (await readContext.Set<ModelDeploymentEndpoint>().AnyAsync(e => e.EndpointId == endpoint.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ForUnknownId_DoesNotThrow()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repository = new ModelDeploymentRepository(dbContext);

        var act = () => repository.DeleteAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }
}
