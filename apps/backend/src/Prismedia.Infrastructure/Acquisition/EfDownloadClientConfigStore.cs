using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>EF-backed store for download client configurations, keeping passwords and API keys in the credential table out of summaries.</summary>
public sealed class EfDownloadClientConfigStore(PrismediaDbContext db) : IDownloadClientConfigStore {
    public async Task<IReadOnlyList<DownloadClientSummary>> ListAsync(CancellationToken cancellationToken) {
        var details = await ListDetailsAsync(cancellationToken);
        return details.Select(ToSummary).ToArray();
    }

    public async Task<IReadOnlyList<DownloadClientDetail>> ListDetailsAsync(CancellationToken cancellationToken) {
        var rows = await db.DownloadClientConfigs
            .AsNoTracking()
            .OrderBy(row => row.DisplayName)
            .ToArrayAsync(cancellationToken);
        var credentials = await CredentialsAsync(rows.Select(row => row.Id).ToArray(), cancellationToken);
        return rows.Select(row => ToDetail(row, credentials.GetValueOrDefault(row.Id))).ToArray();
    }

    public async Task<DownloadClientDetail?> GetAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.DownloadClientConfigs.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        return row is null ? null : ToDetail(row, await CredentialAsync(id, cancellationToken));
    }

    public async Task<DownloadClientDetail?> GetDefaultAsync(CancellationToken cancellationToken) {
        var row = await db.DownloadClientConfigs
            .AsNoTracking()
            .Where(client => client.Enabled)
            .OrderBy(client => client.Priority)
            .ThenBy(client => client.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToDetail(row, await CredentialAsync(row.Id, cancellationToken));
    }

    public async Task<DownloadClientDetail?> GetDefaultAsync(DownloadProtocol protocol, CancellationToken cancellationToken) =>
        (await ListEnabledAsync(protocol, cancellationToken)).FirstOrDefault();

    public async Task<IReadOnlyList<DownloadClientDetail>> ListEnabledAsync(DownloadProtocol protocol, CancellationToken cancellationToken) {
        // The protocol is derived from the kind, which EF can't translate; the client table is tiny.
        var rows = await db.DownloadClientConfigs
            .AsNoTracking()
            .Where(client => client.Enabled)
            .OrderBy(client => client.Priority)
            .ThenBy(client => client.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var matching = rows.Where(client => client.Kind.Protocol() == protocol).ToArray();
        if (matching.Length == 0) {
            return [];
        }

        var credentials = await CredentialsAsync(matching.Select(row => row.Id).ToArray(), cancellationToken);
        return matching.Select(row => ToDetail(row, credentials.GetValueOrDefault(row.Id))).ToArray();
    }

    public async Task<IReadOnlyList<DownloadProtocol>> GetEnabledProtocolsAsync(CancellationToken cancellationToken) {
        var kinds = await db.DownloadClientConfigs
            .AsNoTracking()
            .Where(client => client.Enabled)
            .Select(client => client.Kind)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        return kinds.Select(kind => kind.Protocol()).Distinct().ToArray();
    }

    public async Task<DownloadClientSummary> SaveAsync(DownloadClientSaveCommand command, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = command.Id is { } id
            ? await db.DownloadClientConfigs.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            : null;

        if (row is null) {
            row = new DownloadClientConfigRow {
                Id = command.Id ?? Guid.NewGuid(),
                CreatedAt = now
            };
            db.DownloadClientConfigs.Add(row);
        }

        row.Kind = command.Kind;
        row.DisplayName = command.DisplayName;
        row.BaseUrl = command.BaseUrl;
        row.Username = command.Username;
        row.Category = command.Category;
        row.Priority = command.Priority;
        row.SeedRatio = command.SeedRatio is > 0 ? command.SeedRatio : null;
        row.SeedTimeMinutes = command.SeedTimeMinutes is > 0 ? command.SeedTimeMinutes : null;
        row.Enabled = command.Enabled;
        row.UpdatedAt = now;

        await UpsertCredentialAsync(row.Id, AcquisitionHttp.DownloadClientPasswordCredential, command.Password, now, cancellationToken);
        await UpsertCredentialAsync(row.Id, AcquisitionHttp.DownloadClientApiKeyCredential, command.ApiKey, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToSummary(ToDetail(row, await CredentialAsync(row.Id, cancellationToken)));
    }

    /// <summary>Writes a secret under its credential key; a blank value keeps the stored secret (the UI sends blank for "unchanged").</summary>
    private async Task UpsertCredentialAsync(Guid clientId, string credentialKey, string? value, DateTimeOffset now, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        var credential = await db.DownloadClientCredentials.FirstOrDefaultAsync(candidate =>
            candidate.DownloadClientConfigId == clientId && candidate.CredentialKey == credentialKey,
            cancellationToken);
        if (credential is null) {
            credential = new DownloadClientCredentialRow {
                Id = Guid.NewGuid(),
                DownloadClientConfigId = clientId,
                CredentialKey = credentialKey,
                CreatedAt = now
            };
            db.DownloadClientCredentials.Add(credential);
        }

        credential.EncryptedValue = value;
        credential.UpdatedAt = now;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.DownloadClientConfigs.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null) {
            return false;
        }

        db.DownloadClientConfigs.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>A client's stored secrets: the password and API key credentials.</summary>
    private sealed record ClientCredentials(string? Password, string? ApiKey);

    private async Task<Dictionary<Guid, ClientCredentials>> CredentialsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) {
        var rows = await db.DownloadClientCredentials
            .AsNoTracking()
            .Where(row => ids.Contains(row.DownloadClientConfigId))
            .ToArrayAsync(cancellationToken);
        return rows
            .GroupBy(row => row.DownloadClientConfigId)
            .ToDictionary(group => group.Key, group => new ClientCredentials(
                group.FirstOrDefault(row => row.CredentialKey == AcquisitionHttp.DownloadClientPasswordCredential)?.EncryptedValue,
                group.FirstOrDefault(row => row.CredentialKey == AcquisitionHttp.DownloadClientApiKeyCredential)?.EncryptedValue));
    }

    private async Task<ClientCredentials> CredentialAsync(Guid id, CancellationToken cancellationToken) =>
        (await CredentialsAsync([id], cancellationToken)).GetValueOrDefault(id) ?? new ClientCredentials(null, null);

    private static DownloadClientSummary ToSummary(DownloadClientDetail detail) =>
        new(detail.Id, detail.Kind, detail.DisplayName, detail.BaseUrl, detail.Username, detail.Category, detail.Enabled, detail.HasPassword, detail.ApiKey is not null and not "", detail.Priority,
            detail.SeedRatio, detail.SeedTimeMinutes);

    private static DownloadClientDetail ToDetail(DownloadClientConfigRow row, ClientCredentials? credentials) =>
        new(row.Id, row.Kind, row.DisplayName, row.BaseUrl, row.Username, row.Category, row.Enabled,
            !string.IsNullOrEmpty(credentials?.Password), credentials?.Password, credentials?.ApiKey, row.Priority, row.SeedRatio, row.SeedTimeMinutes);
}
