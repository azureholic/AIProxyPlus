using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Domain.Exceptions;
using FluentAssertions;

namespace AIProxyPlus.Domain.Tests.Entities;

public class ApiRouteTests
{
    [Fact]
    public void Create_WithValidValues_CreatesRoute()
    {
        var route = ApiRoute.Create(Guid.NewGuid(), "Foundry OpenAI", Provider.OpenAI, "/openai/v1");

        route.Name.Should().Be("Foundry OpenAI");
        route.Provider.Should().Be(Provider.OpenAI);
        route.PathPrefix.Should().Be("/openai/v1");
        route.IsBuiltIn.Should().BeFalse();
    }

    [Theory]
    [InlineData("openai/v1/", "/openai/v1")]
    [InlineData("//openai//v1//", "/openai/v1")]
    [InlineData("/", "/")]
    public void Create_WithNonCanonicalPath_NormalizesPathPrefix(string pathPrefix, string expected)
    {
        var route = ApiRoute.Create(Guid.NewGuid(), "Route", Provider.OpenAI, pathPrefix);

        route.PathPrefix.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://api.example.com/v1")]
    [InlineData("/v1?api-version=1")]
    [InlineData("/v1#fragment")]
    public void Create_WithNonPathPrefix_Throws(string pathPrefix)
    {
        var act = () => ApiRoute.Create(Guid.NewGuid(), "Route", Provider.OpenAI, pathPrefix);

        act.Should().Throw<DomainException>().WithMessage("*PathPrefix*");
    }

    [Fact]
    public void Update_BuiltInRoute_PreservesBuiltInOrigin()
    {
        var route = ApiRoute.Create(Guid.NewGuid(), "Foundry OpenAI", Provider.OpenAI, "/openai/v1", isBuiltIn: true);

        route.Update("Foundry MAI", Provider.MicrosoftAI, "/mai/v1");

        route.Name.Should().Be("Foundry MAI");
        route.Provider.Should().Be(Provider.MicrosoftAI);
        route.PathPrefix.Should().Be("/mai/v1");
        route.IsBuiltIn.Should().BeTrue();
    }
}