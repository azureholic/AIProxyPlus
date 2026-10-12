using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Application.Models;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace AIProxyPlus.Application.Tests.Models;

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
    public async Task HandleAsync_WithCapabilities_PersistsCapabilitySet()
    {
        _repository.ExistsByNameAsync("whisper", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreateModelCommand("whisper", Capabilities: [ModelCapability.SpeechToText]));

        result.Capabilities.Should().Equal(ModelCapability.SpeechToText);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyCapabilities_ThrowsValidationException()
    {
        var act = () => _handler.HandleAsync(new CreateModelCommand("whisper", Capabilities: []));

        await act.Should().ThrowAsync<ValidationException>();
        await _repository.DidNotReceive().AddAsync(Arg.Any<Model>(), Arg.Any<CancellationToken>());
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
