using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;
using FluentAssertions;

namespace AiProxy.Domain.Tests.Entities;

public class ModelDeploymentTests
{
    private static Endpoint CreateEndpoint(Guid modelDeploymentId, int priority) =>
        Endpoint.Create(
            Guid.NewGuid(), modelDeploymentId, "https://api.openai.com", ApiFamily.OpenAI, priority,
            AuthMode.ApiKey, secretRef: "secret/x");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyName_Throws(string name)
    {
        var act = () => ModelDeployment.Create(Guid.NewGuid(), name);

        act.Should().Throw<DomainException>().WithMessage("*Name*");
    }

    [Fact]
    public void HasUsableEndpoint_WithNoEndpoints_IsFalse()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");

        deployment.HasUsableEndpoint.Should().BeFalse();
    }

    [Fact]
    public void AddEndpoint_ThenHasUsableEndpoint_IsTrue()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        deployment.AddEndpoint(CreateEndpoint(deployment.Id, priority: 1));

        deployment.HasUsableEndpoint.Should().BeTrue();
        deployment.Endpoints.Should().HaveCount(1);
    }

    [Fact]
    public void AddEndpoint_WithDuplicatePriority_IsAllowed_ForRoundRobin()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        deployment.AddEndpoint(CreateEndpoint(deployment.Id, priority: 1));

        deployment.AddEndpoint(CreateEndpoint(deployment.Id, priority: 1));

        deployment.Endpoints.Should().HaveCount(2);
        deployment.Endpoints.Should().OnlyContain(e => e.Priority == 1);
    }

    [Fact]
    public void AddEndpoint_WithMismatchedModelDeploymentId_Throws()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        var foreignEndpoint = CreateEndpoint(Guid.NewGuid(), priority: 1);

        var act = () => deployment.AddEndpoint(foreignEndpoint);

        act.Should().Throw<DomainException>().WithMessage("*ModelDeploymentId*");
    }

    [Fact]
    public void RemoveEndpoint_RemovesIt()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        var endpoint = CreateEndpoint(deployment.Id, priority: 1);
        deployment.AddEndpoint(endpoint);

        deployment.RemoveEndpoint(endpoint.Id);

        deployment.Endpoints.Should().BeEmpty();
        deployment.HasUsableEndpoint.Should().BeFalse();
    }

    [Fact]
    public void ChangeEndpointPriority_WithUnusedPriority_Succeeds()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        var endpoint = CreateEndpoint(deployment.Id, priority: 1);
        deployment.AddEndpoint(endpoint);

        deployment.ChangeEndpointPriority(endpoint.Id, 5);

        endpoint.Priority.Should().Be(5);
    }

    [Fact]
    public void ChangeEndpointPriority_ToPriorityUsedByAnotherEndpoint_IsAllowed_ForRoundRobin()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        var first = CreateEndpoint(deployment.Id, priority: 1);
        var second = CreateEndpoint(deployment.Id, priority: 2);
        deployment.AddEndpoint(first);
        deployment.AddEndpoint(second);

        deployment.ChangeEndpointPriority(second.Id, 1);

        second.Priority.Should().Be(1);
    }

    [Fact]
    public void ChangeEndpointPriority_ForUnknownEndpoint_Throws()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");

        var act = () => deployment.ChangeEndpointPriority(Guid.NewGuid(), 2);

        act.Should().Throw<DomainException>();
    }
}
