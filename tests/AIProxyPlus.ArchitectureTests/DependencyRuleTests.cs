using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace AIProxyPlus.ArchitectureTests;

/// <summary>
/// Enforces the Clean Architecture dependency rule from architecture.md §4: Domain and Application
/// must never depend on outer layers or infrastructure-facing frameworks.
/// </summary>
public class DependencyRuleTests
{
    private static readonly Assembly DomainAssembly = Assembly.Load("AIProxyPlus.Domain");
    private static readonly Assembly ApplicationAssembly = Assembly.Load("AIProxyPlus.Application");

    [Fact]
    public void Domain_Should_Not_Depend_On_Infrastructure()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("AIProxyPlus.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FormatFailures(result));
    }

    [Fact]
    public void Domain_Should_Not_Depend_On_Api()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("AIProxyPlus.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FormatFailures(result));
    }

    [Fact]
    public void Domain_Should_Not_Depend_On_AspNetCore()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FormatFailures(result));
    }

    [Fact]
    public void Domain_Should_Not_Depend_On_EfCore()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FormatFailures(result));
    }

    [Fact]
    public void Application_Should_Not_Depend_On_Infrastructure()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("AIProxyPlus.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FormatFailures(result));
    }

    [Fact]
    public void Application_Should_Not_Depend_On_Api()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("AIProxyPlus.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FormatFailures(result));
    }

    private static string FormatFailures(TestResult result) =>
        result.FailingTypeNames is { Count: > 0 }
            ? $"Unexpected dependency found in: {string.Join(", ", result.FailingTypeNames)}"
            : "Dependency rule violated.";
}
