using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Prismedia.Application.Integrations;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Protects connection secrets with a persistent key ring shared by API and worker, and connection-specific purposes.</summary>
public sealed class ConnectionSecretProtector {
    private const string ApplicationName = "Prismedia.Connections";
    private const string Purpose = "connection-credentials-v1";
    private readonly IDataProtectionProvider _provider;

    /// <summary>Uses the private key directory beneath persistent application data, never a transient cache.</summary>
    public ConnectionSecretProtector(string dataDirectory) {
        var directory = Directory.CreateDirectory(Path.Combine(dataDirectory, "keys", "connections"));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory.FullName,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _provider = DataProtectionProvider.Create(directory, builder => builder.SetApplicationName(ApplicationName));
    }

    /// <summary>Encrypts one value bound to its connection and credential key.</summary>
    public string Protect(Guid connectionId, string key, string value) =>
        _provider.CreateProtector(Purpose, connectionId.ToString("N"), key).Protect(value);

    /// <summary>Decrypts a value only for its original connection and key; missing keys never yield plaintext fallback.</summary>
    public string Unprotect(Guid connectionId, string key, string value) {
        try { return _provider.CreateProtector(Purpose, connectionId.ToString("N"), key).Unprotect(value); }
        catch (CryptographicException) { throw new ConnectionSecretUnavailableException(); }
    }
}
