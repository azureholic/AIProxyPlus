using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.Models;
using AiProxy.Domain.Entities;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace AiProxy.Application.Tests.Models;

public class CreateModelTests
{
    private readonly IModelRepository _repository = Substitute.For<IModelRepository>();
    private readonly CreateModelHandler _handler;

    public CreateModelTests()
    {
        _handler = new CreateModelHandler(_repository, new CreateModelValidator());
    }

    [Fact]
    public async Task HandleAsync_WithNewName_CreatesAndPersistsModel()
    {
        _repository.ExistsByNameAsync("gpt-4o-prod", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(new CreateModelCommand("gpt-4o-prod"));

        result.Name.Should().Be("gpt-4o-prod");
        await _repository.Received(1).AddAsync(Arg.Is<Model>(d => d.Name == "gpt-4o-prod"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithExistingName_ThrowsConflictException()
    {
        _repository.ExistsByNameAsync("gpt-4o-prod", Arg.Any<CancellationToken>()).Returns(true);

        var act = () => _handler.HandleAsync(new CreateModelCommand("gpt-4o-prod"));

        await act.Should().ThrowAsync<ConflictException>();
        await _repository.DidNotReceive().AddAsync(Arg.Any<Model>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithEmptyName_ThrowsValidationException(string name)
    {
        var act = () => _handler.HandleAsync(new CreateModelCommand(name));

        await act.Should().ThrowAsync<ValidationException>();
    }
}
