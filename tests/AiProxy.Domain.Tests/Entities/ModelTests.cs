using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;
using FluentAssertions;

namespace AiProxy.Domain.Tests.Entities;

public class ModelTests
{
    private static ModelEndpoint CreateEndpoint(Guid modelId, int priority, Endpoint? endpoint = null) =>
        ModelEndpoint.Create(
            modelId,
            endpoint ?? Endpoint.Create(Guid.NewGuid(), "OpenAI", "https://api.openai.com", ApiFamily.OpenAI,
                AuthMode.ApiKey, secretRef: "secret/x"),
            priority);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyName_Throws(string name)
    {
        var act = () => Model.Create(Guid.NewGuid(), name);

        act.Should().Throw<DomainException>().WithMessage("*Name*");
    }

    [Fact]
    public void HasUsableEndpoint_WithNoEndpoints_IsFalse()
    {
        var model = Model.Create(Guid.NewGuid(), "gpt-4o-prod");

        model.HasUsableEndpoint.Should().BeFalse();
    }

    [Fact]
    public void AddEndpoint_ThenHasUsableEndpoint_IsTrue()
    {
        var model = Model.Create(Guid.NewGuid(), "gpt-4o-prod");
        model.AddEndpoint(CreateEndpoint(model.Id, priority: 1));

        model.HasUsableEndpoint.Should().BeTrue();
        model.Endpoints.Should().HaveCount(1);
    }

    [Fact]
    public void AddEndpoint_WithDuplicatePriority_IsAllowed_ForRoundRobin()
    {
        var model = Model.Create(Guid.NewGuid(), "gpt-4o-prod");
        model.AddEndpoint(CreateEndpoint(model.Id, priority: 1));

        model.AddEndpoint(CreateEndpoint(model.Id, priority: 1));

        model.Endpoints.Should().HaveCount(2);
        model.Endpoints.Should().OnlyContain(e => e.Priority == 1);
    }

    [Fact]
    public void AddEndpoint_WithMismatchedModelId_Throws()
    {
        var model = Model.Create(Guid.NewGuid(), "gpt-4o-prod");
        var foreignEndpoint = CreateEndpoint(Guid.NewGuid(), priority: 1);

        var act = () => model.AddEndpoint(foreignEndpoint);

        act.Should().Throw<DomainException>().WithMessage("*ModelId*");
    }

    [Fact]
    public void AddEndpoint_SameReusableEndpointAcrossModels_Succeeds()
    {
        var endpoint = Endpoint.Create(Guid.NewGuid(), "Shared", "https://api.openai.com", ApiFamily.OpenAI,
            AuthMode.ApiKey, secretRef: "secret/x");
        var first = Model.Create(Guid.NewGuid(), "gpt-4o");
        var second = Model.Create(Guid.NewGuid(), "gpt-4.1");

        first.AddEndpoint(CreateEndpoint(first.Id, 1, endpoint));
        second.AddEndpoint(CreateEndpoint(second.Id, 2, endpoint));

        first.Endpoints.Single().Endpoint.Should().BeSameAs(endpoint);
        second.Endpoints.Single().Endpoint.Should().BeSameAs(endpoint);
    }

    [Fact]
    public void AddEndpoint_DuplicateAssignment_Throws()
    {
        var endpoint = Endpoint.Create(Guid.NewGuid(), "Shared", "https://api.openai.com", ApiFamily.OpenAI,
            AuthMode.ApiKey, secretRef: "secret/x");
        var model = Model.Create(Guid.NewGuid(), "gpt-4o");
        model.AddEndpoint(CreateEndpoint(model.Id, 1, endpoint));

        var act = () => model.AddEndpoint(CreateEndpoint(model.Id, 2, endpoint));

        act.Should().Throw<DomainException>().WithMessage("*already assigned*");
    }

    [Fact]
    public void RemoveEndpoint_RemovesIt()
    {
        var model = Model.Create(Guid.NewGuid(), "gpt-4o-prod");
        var endpoint = CreateEndpoint(model.Id, priority: 1);
        model.AddEndpoint(endpoint);

        model.RemoveEndpoint(endpoint.Id);

        model.Endpoints.Should().BeEmpty();
        model.HasUsableEndpoint.Should().BeFalse();
    }

    [Fact]
    public void ChangeEndpointPriority_WithUnusedPriority_Succeeds()
    {
        var model = Model.Create(Guid.NewGuid(), "gpt-4o-prod");
        var endpoint = CreateEndpoint(model.Id, priority: 1);
        model.AddEndpoint(endpoint);

        model.ChangeEndpointPriority(endpoint.Id, 5);

        endpoint.Priority.Should().Be(5);
    }

    [Fact]
    public void ChangeEndpointPriority_ToPriorityUsedByAnotherEndpoint_IsAllowed_ForRoundRobin()
    {
        var model = Model.Create(Guid.NewGuid(), "gpt-4o-prod");
        var first = CreateEndpoint(model.Id, priority: 1);
        var second = CreateEndpoint(model.Id, priority: 2);
        model.AddEndpoint(first);
        model.AddEndpoint(second);

        model.ChangeEndpointPriority(second.Id, 1);

        second.Priority.Should().Be(1);
    }

    [Fact]
    public void ChangeEndpointPriority_ForUnknownEndpoint_Throws()
    {
        var model = Model.Create(Guid.NewGuid(), "gpt-4o-prod");

        var act = () => model.ChangeEndpointPriority(Guid.NewGuid(), 2);

        act.Should().Throw<DomainException>();
    }
}
