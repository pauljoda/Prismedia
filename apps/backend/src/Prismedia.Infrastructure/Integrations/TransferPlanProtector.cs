using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Prismedia.Application.Integrations;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Encrypts accepted source locators and executor inputs under operation-specific purposes.</summary>
public sealed class TransferPlanProtector {
    #region Static Variables

    private const string Purpose = "integration-transfer-plan-v1";

    #endregion

    #region Variables

    private readonly IDataProtectionProvider provider;

    #endregion

    #region Constructors

    /// <summary>Uses the same persistent key directory as connections, with a distinct protection purpose.</summary>
    public TransferPlanProtector(string dataDirectory) => provider = IntegrationDataProtection.Create(dataDirectory);

    #endregion

    #region Actions - Protection

    /// <summary>Protects an immutable accepted intent for exactly one connection and operation.</summary>
    public string Protect(Guid connectionId, Guid operationId, string value) =>
        provider.CreateProtector(Purpose, connectionId.ToString("N"), operationId.ToString("N")).Protect(value);

    /// <summary>Restores only the original intent; missing keys or cross-operation ciphertext never yield plaintext fallback.</summary>
    public string Unprotect(Guid connectionId, Guid operationId, string value) {
        try {
            return provider.CreateProtector(Purpose, connectionId.ToString("N"), operationId.ToString("N")).Unprotect(value);
        } catch (CryptographicException) {
            throw new IntegrationTransferPlanUnavailableException();
        }
    }

    #endregion
}
