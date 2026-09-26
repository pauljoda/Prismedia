using Microsoft.EntityFrameworkCore;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Security;

/// <summary>Shared credential encryption boundary for metadata plugins and native subtitle providers.</summary>
public sealed class ProviderCredentialStore(PrismediaDbContext db, ProviderCredentialProtector protector) {
    /// <summary>Stages one encrypted value in the caller's unit of work without saving unrelated changes.</summary>
    public void SetValue(ProviderCredentialRow row, string value, DateTimeOffset now) {
        row.EncryptedValue = protector.Protect(row.ProviderConfigId, row.CredentialKey, value);
        row.ProtectionVersion = ProviderCredentialProtector.CurrentVersion;
        row.UpdatedAt = now;
    }

    /// <summary>Reads only the requested credential keys, upgrading legacy values before returning secrets to their provider.</summary>
    public async Task<Dictionary<string, string>> ReadAsync(Guid providerId, IReadOnlyCollection<string> allowedKeys, CancellationToken token) {
        var keys = allowedKeys.Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length == 0) return new(StringComparer.Ordinal);
        var rows = await db.ProviderCredentials.AsNoTracking()
            .Where(row => row.ProviderConfigId == providerId && keys.Contains(row.CredentialKey)).ToArrayAsync(token);
        if (rows.Any(row => row.ProtectionVersion == ProviderCredentialProtector.LegacyVersion)) {
            foreach (var row in rows.Where(row => row.ProtectionVersion == ProviderCredentialProtector.LegacyVersion)) await UpgradeAsync(row, token);
            rows = await db.ProviderCredentials.AsNoTracking()
                .Where(row => row.ProviderConfigId == providerId && keys.Contains(row.CredentialKey)).ToArrayAsync(token);
        }
        return rows.ToDictionary(row => row.CredentialKey,
            row => protector.Unprotect(row.ProviderConfigId, row.CredentialKey, row.EncryptedValue, row.ProtectionVersion), StringComparer.Ordinal);
    }

    /// <summary>Encrypts existing rows in bounded batches, including disabled providers, without overwriting concurrent credential edits.</summary>
    public async Task UpgradeLegacyAsync(CancellationToken token) {
        while (true) {
            var rows = await db.ProviderCredentials.AsNoTracking()
                .Where(row => row.ProtectionVersion == ProviderCredentialProtector.LegacyVersion)
                .OrderBy(row => row.Id).Take(100).ToArrayAsync(token);
            if (rows.Length == 0) return;
            foreach (var row in rows) await UpgradeAsync(row, token);
        }
    }

    private async Task UpgradeAsync(ProviderCredentialRow original, CancellationToken token) {
        var encrypted = protector.Protect(original.ProviderConfigId, original.CredentialKey, original.EncryptedValue);
        if (db.Database.IsRelational()) {
            // Both processes may migrate at startup. Compare against the exact observed value so a
            // concurrent save/delete wins without stale secret resurrection or overwriting a new value.
            await db.ProviderCredentials.Where(row => row.Id == original.Id &&
                    row.ProviderConfigId == original.ProviderConfigId && row.CredentialKey == original.CredentialKey &&
                    row.ProtectionVersion == ProviderCredentialProtector.LegacyVersion && row.EncryptedValue == original.EncryptedValue)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.EncryptedValue, encrypted)
                    .SetProperty(row => row.ProtectionVersion, ProviderCredentialProtector.CurrentVersion), token);
            return;
        }
        var tracked = await db.ProviderCredentials.FindAsync([original.Id], token);
        if (tracked is null || tracked.ProtectionVersion != ProviderCredentialProtector.LegacyVersion || tracked.EncryptedValue != original.EncryptedValue) return;
        tracked.EncryptedValue = encrypted;
        tracked.ProtectionVersion = ProviderCredentialProtector.CurrentVersion;
        await db.SaveChangesAsync(token);
    }
}
