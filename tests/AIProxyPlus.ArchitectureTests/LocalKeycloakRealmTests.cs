using System.Text.Json;
using FluentAssertions;

namespace AIProxyPlus.ArchitectureTests;

public sealed class LocalKeycloakRealmTests
{
    [Fact]
    public void ProxyAdmin_WhenRealmIsReimported_HasStableSubject()
    {
        var realmPath = Path.Combine(AppContext.BaseDirectory, "keycloak", "aiproxyplus-local-realm.json");
        using var realm = JsonDocument.Parse(File.ReadAllText(realmPath));

        var proxyAdmin = realm.RootElement
            .GetProperty("users")
            .EnumerateArray()
            .Single(user => user.GetProperty("username").GetString() == "proxy-admin");

        proxyAdmin.GetProperty("id").GetString()
            .Should().Be("6c1b7227-3af9-445d-bc8f-613c723e9c49");
    }
}