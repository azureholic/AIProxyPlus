using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.ModelDeployments;
using AiProxy.Domain.Entities;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace AiProxy.Application.Tests.ModelDeployments;

public class CreateModelDeploymentTests
{
    private readonly IModelDeploymentRepository _repository = Substitute.For<IModelDeploymentRepository>();
    private readonly CreateModelDeploymentHandler _handler;

    public CreateModelDeploymentTests()
    {
        _handler = new CreateModelDeploymentHandler(_repository, new CreateModelDeploymentValidator());
    }

    [Fact]
    public async Task HandleAsync_WithNewName_CreatesAndPersistsDeployment()
    {
        _repository.ExistsByNameAsync("gpt-4o-prod", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(new CreateModelDeploymentCommand("gpt-4o-prod"));

        result.Name.Should().Be("gpt-4o-prod");
        await _repository.Received(1).AddAsync(Arg.Is<ModelDeployment>(d => d.Name == "gpt-4o-prod"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithExistingName_ThrowsConflictException()
    {
        _repository.ExistsByNameAsync("gpt-4o-prod", Arg.Any<CancellationToken>()).Returns(true);

        var act = () => _handler.HandleAsync(new CreateModelDeploymentCommand("gpt-4o-prod"));

        await act.Should().ThrowAsync<ConflictException>();
        await _repository.DidNotReceive().AddAsync(Arg.Any<ModelDeployment>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithEmptyName_ThrowsValidationException(string name)
    {
        var act = () => _handler.HandleAsync(new CreateModelDeploymentCommand(name));

        await act.Should().ThrowAsync<ValidationException>();
    }
}
