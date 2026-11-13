using System.Security.Cryptography;
using System.Xml.Linq;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Infrastructure.Security;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AIProxyPlus.Infrastructure.Tests.Secrets;

public sealed class DataProtectionKeyRingTests
{
    [Fact]
    public void Protect_SeparateReplicaAndRestart_CanUnprotectWithoutPersistingPlaintextKeyMaterial()
    {
        var repository = new SharedRepository();
        var material = new Dictionary<string, string>();
        var secrets = Substitute.For<ISecretResolver>();
        secrets.StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                material.Add(call.ArgAt<string>(0), call.ArgAt<string>(1));
                return Task.CompletedTask;
            });
        secrets.TryResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => material.GetValueOrDefault(call.ArgAt<string>(0)));

        string payload;
        using (var first = CreateReplica(repository, secrets))
        {
            payload = first.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("session").Protect("browser-session");
        }
        using var replacement = CreateReplica(repository, secrets);

        replacement.GetRequiredService<IDataProtectionProvider>().CreateProtector("session")
            .Unprotect(payload).Should().Be("browser-session");
        material.Should().NotBeEmpty();
        repository.GetAllElements().Select(key => key.ToString()).Should().OnlyContain(xml =>
            !xml.Contains("<masterKey") && xml.Contains("data-protection/"));
    }

    [Fact]
    public void Encrypt_SecretStoreUnavailable_PropagatesFailureWithoutWritingReference()
    {
        var secrets = Substitute.For<ISecretResolver>();
        secrets.StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Secret store unavailable")));

        var act = () => new SecretStoreXmlEncryptor(secrets).Encrypt(new XElement("masterKey", "sensitive"));

        act.Should().Throw<InvalidOperationException>().WithMessage("Secret store unavailable");
    }

    [Fact]
    public void Decrypt_MissingKeyMaterial_FailsExplicitly()
    {
        var secrets = Substitute.For<ISecretResolver>();
        secrets.TryResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        using var services = new ServiceCollection().AddSingleton(secrets).BuildServiceProvider();
        var decryptor = new SecretStoreXmlDecryptor(services);

        var act = () => decryptor.Decrypt(new XElement("secretStore",
            new XAttribute("reference", $"data-protection/{Guid.NewGuid():N}")));

        act.Should().Throw<CryptographicException>().WithMessage("*missing*");
    }

    [Fact]
    public void Decrypt_ReferenceOutsideKeyNamespace_RejectsBeforeResolving()
    {
        var secrets = Substitute.For<ISecretResolver>();
        using var services = new ServiceCollection().AddSingleton(secrets).BuildServiceProvider();

        var act = () => new SecretStoreXmlDecryptor(services).Decrypt(
            new XElement("secretStore", new XAttribute("reference", "consumer-key-pepper")));

        act.Should().Throw<CryptographicException>().WithMessage("*Invalid*");
        secrets.DidNotReceive().TryResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static ServiceProvider CreateReplica(IXmlRepository repository, ISecretResolver secrets)
    {
        var services = new ServiceCollection();
        services.AddSingleton(secrets);
        services.AddDataProtection().SetApplicationName("AIProxyPlus");
        services.AddOptions<KeyManagementOptions>().Configure(options =>
        {
            options.XmlRepository = repository;
            options.XmlEncryptor = new SecretStoreXmlEncryptor(secrets);
        });
        return services.BuildServiceProvider();
    }

    private sealed class SharedRepository : IXmlRepository
    {
        private readonly List<XElement> _keys = [];

        public IReadOnlyCollection<XElement> GetAllElements() => _keys.Select(key => new XElement(key)).ToArray();

        public void StoreElement(XElement element, string friendlyName) => _keys.Add(new XElement(element));
    }
}
