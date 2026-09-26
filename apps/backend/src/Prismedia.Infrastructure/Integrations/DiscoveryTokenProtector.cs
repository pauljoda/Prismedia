using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>
/// Seals short-lived source navigation and selection tokens without exposing source locators or accepting client-authored URLs.
/// </summary>
public sealed class DiscoveryTokenProtector(string dataDirectory) : IDiscoveryTokenProtector {
    #region Static Variables

    private const string SelectionPurpose = "catalog-selection-v1";

    private const string CursorPurpose = "catalog-cursor-v1";

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    #endregion

    #region Variables

    private readonly IDataProtectionProvider _provider = IntegrationDataProtection.Create(dataDirectory);

    #endregion

    #region Actions - Selections

    /// <inheritdoc />
    public string ProtectSelection(Guid connectionId, SourceSelection selection) =>
        Protector(SelectionPurpose, connectionId)
            .Protect(JsonSerializer.Serialize(selection, PluginProcessTransport.JsonOptions), Lifetime);

    /// <inheritdoc />
    public SourceSelection ReadSelection(Guid connectionId, string token) {
        try {
            return JsonSerializer.Deserialize<SourceSelection>(Read(Protector(SelectionPurpose, connectionId), token),
                PluginProcessTransport.JsonOptions) ?? throw InvalidToken();
        } catch (JsonException) {
            throw InvalidToken();
        }
    }

    #endregion

    #region Actions - Cursors

    /// <inheritdoc />
    public string ProtectCursor(Guid connectionId, BrowseConnectionRequest scope, string cursor) =>
        Protector(CursorPurpose, connectionId, Scope(scope)).Protect(cursor, Lifetime);

    /// <inheritdoc />
    public string ReadCursor(Guid connectionId, BrowseConnectionRequest scope, string token) =>
        Read(Protector(CursorPurpose, connectionId, Scope(scope)), token);

    private static string Scope(BrowseConnectionRequest request) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request with { Cursor = null }, PluginProcessTransport.JsonOptions))));

    #endregion

    #region Actions - Protection

    private ITimeLimitedDataProtector Protector(string purpose, Guid id, string scope = "") =>
        _provider.CreateProtector(purpose, id.ToString("N"), scope).ToTimeLimitedDataProtector();

    private static string Read(ITimeLimitedDataProtector protector, string token) {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 32_768) {
            throw InvalidToken();
        }

        try {
            return protector.Unprotect(token);
        } catch (CryptographicException) {
            throw InvalidToken();
        }
    }

    private static ArgumentException InvalidToken() =>
        new("This catalog selection or page token is invalid or expired. Refresh the catalog and select it again.");

    #endregion
}
