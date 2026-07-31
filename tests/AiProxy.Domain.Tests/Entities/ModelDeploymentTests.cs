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
    public void AddEndpoint_WithDuplicatePriority_Throws()
    {
        var deployment = ModelDeployment.Create(Guid.NewGuid(), "gpt-4o-prod");
        deployment.AddEndpoint(CreateEndpoint(deployment.Id, priority: 1));

        var act = () => deployment.AddEndpoint(CreateEndpoint(deployment.Id, priority: 1));

        act.Should().Throw<DomainException>().WithMessage("*Priority*");
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
}
