using System.Text.RegularExpressions;
using FluentAssertions;
using Npgsql;

namespace AIProxyPlus.Infrastructure.Tests.Persistence;

public sealed class PostgresDeploymentConnectionStringTests
{
    [Theory]
    [InlineData("Abcd;xyz123!")]
    [InlineData(" abc\";SSL Mode=Disable;Password=\"xyz ")]
    public void PublishedConnectionStrings_SpecialCredentialCharacters_PreserveValuesAndRequireTls(string password)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AIProxyPlus.sln")))
        {
            root = root.Parent;
        }
        root.Should().NotBeNull();
        var module = File.ReadAllText(Path.Combine(root!.FullName, "infra", "postgres", "postgres.module.bicep"));
        var templates = Regex.Matches(module, @"value: '(Host=.*)'");
        templates.Count.Should().Be(2);
        const string username = "user;\"name";

        foreach (Match template in templates)
        {
            var value = template.Groups[1].Value
                .Replace("${postgres.properties.fullyQualifiedDomainName}", "postgres.example.com")
                .Replace("${replace(administratorLogin, '\"', '\"\"')}", username.Replace("\"", "\"\""))
                .Replace("${replace(administratorLoginPassword, '\"', '\"\"')}", password.Replace("\"", "\"\""));
            var connection = new NpgsqlConnectionStringBuilder(value);

            connection.Username.Should().Be(username);
            connection.Password.Should().Be(password);
            connection.SslMode.Should().Be(SslMode.Require);
            connection.Host.Should().Be("postgres.example.com");
        }
    }
}
