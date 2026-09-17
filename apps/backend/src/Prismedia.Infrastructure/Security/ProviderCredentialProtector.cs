using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Prismedia.Infrastructure.Integrations;

namespace Prismedia.Infrastructure.Security;

/// <summary>Encrypts saved provider credentials with persistent application keys and provider/key-specific purposes.</summary>
public sealed class ProviderCredentialProtector {
    /// <summary>Existing rows without a protection version contain legacy plaintext.</summary>
    public const int LegacyVersion = 0;
    /// <summary>Current authenticated encryption format; unknown versions never fall back to plaintext.</summary>
    public const int CurrentVersion = 1;
    private const string Purpose = "provider-credentials-v1";
    private readonly IDataProtectionProvider _provider;

    /// <summary>Uses the persistent private application key ring shared by the API and worker.</summary>
    public ProviderCredentialProtector(string dataDirectory) : this(IntegrationDataProtection.Create(dataDirectory)) { }
    internal ProviderCredentialProtector(IDataProtectionProvider provider) => _provider = provider;

    /// <summary>Protects one secret for its original provider configuration and credential key.</summary>
    public string Protect(Guid providerId, string key, string value) =>
        _provider.CreateProtector(Purpose, providerId.ToString("N"), key).Protect(value);

    /// <summary>Decrypts only the supported protected format, without returning ciphertext or silently accepting missing keys.</summary>
    public string Unprotect(Guid providerId, string key, string value, int version) {
        if (version != CurrentVersion) throw Unavailable();
        try { return _provider.CreateProtector(Purpose, providerId.ToString("N"), key).Unprotect(value); }
        catch (CryptographicException) { throw Unavailable(); }
    }

    private static InvalidOperationException Unavailable() =>
        new("Saved provider credentials could not be decrypted. Restore the application keys or enter the credentials again.");
}
