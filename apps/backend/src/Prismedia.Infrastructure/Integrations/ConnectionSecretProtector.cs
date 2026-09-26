using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Prismedia.Application.Integrations;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Protects connection secrets with a persistent key ring shared by API and worker, and connection-specific purposes.</summary>
public sealed class ConnectionSecretProtector {
    #region Static Variables

    private const string Purpose = "connection-credentials-v1";

    #endregion

    #region Variables

    private readonly IDataProtectionProvider _provider;

    #endregion

    #region Constructors

    /// <summary>Uses the private key directory beneath persistent application data, never a transient cache.</summary>
    public ConnectionSecretProtector(string dataDirectory) {
        _provider = IntegrationDataProtection.Create(dataDirectory);
    }

    #endregion

    #region Actions - Protection

    /// <summary>Encrypts one value bound to its connection and credential key.</summary>
    public string Protect(Guid connectionId, string key, string value) =>
        _provider.CreateProtector(Purpose, connectionId.ToString("N"), key).Protect(value);

    /// <summary>Decrypts a value only for its original connection and key; missing keys never yield plaintext fallback.</summary>
    public string Unprotect(Guid connectionId, string key, string value) {
        try {
            return _provider.CreateProtector(Purpose, connectionId.ToString("N"), key).Unprotect(value);
        } catch (CryptographicException) {
            throw new ConnectionSecretUnavailableException();
        }
    }

    #endregion
}
