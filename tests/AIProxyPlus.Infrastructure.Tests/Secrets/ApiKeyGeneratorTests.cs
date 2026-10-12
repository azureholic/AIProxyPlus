using AIProxyPlus.Infrastructure.Secrets;
using FluentAssertions;

namespace AIProxyPlus.Infrastructure.Tests.Secrets;

public class ApiKeyGeneratorTests
{
    [Fact]
    public void Generate_ReturnsKeyWithExpectedPrefix()
    {
        var generator = new ApiKeyGenerator();

        generator.Generate().Should().StartWith("aip_");
    }

    [Fact]
    public void Generate_ProducesUniqueKeysAcrossManyCalls()
    {
        var generator = new ApiKeyGenerator();

        var keys = Enumerable.Range(0, 1000).Select(_ => generator.Generate()).ToHashSet();

        keys.Should().HaveCount(1000);
    }

    [Fact]
    public void Generate_DoesNotContainUrlUnsafeCharacters()
    {
        var generator = new ApiKeyGenerator();

        var key = generator.Generate();

        key.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }
}
