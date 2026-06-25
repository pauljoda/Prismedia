using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>EF-backed store for download client configurations, keeping passwords in the credential table out of summaries.</summary>
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
        var passwords = await PasswordsAsync(rows.Select(row => row.Id).ToArray(), cancellationToken);
        return rows.Select(row => ToDetail(row, passwords.GetValueOrDefault(row.Id))).ToArray();
    }

    public async Task<DownloadClientDetail?> GetAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.DownloadClientConfigs.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        return row is null ? null : ToDetail(row, await PasswordAsync(id, cancellationToken));
    }

    public async Task<DownloadClientDetail?> GetDefaultAsync(CancellationToken cancellationToken) {
        var row = await db.DownloadClientConfigs
            .AsNoTracking()
            .Where(client => client.Enabled)
            .OrderBy(client => client.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToDetail(row, await PasswordAsync(row.Id, cancellationToken));
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
        row.Enabled = command.Enabled;
        row.UpdatedAt = now;

        if (!string.IsNullOrWhiteSpace(command.Password)) {
            var credential = await db.DownloadClientCredentials.FirstOrDefaultAsync(candidate =>
                candidate.DownloadClientConfigId == row.Id && candidate.CredentialKey == AcquisitionHttp.DownloadClientPasswordCredential,
                cancellationToken);
            if (credential is null) {
                credential = new DownloadClientCredentialRow {
                    Id = Guid.NewGuid(),
                    DownloadClientConfigId = row.Id,
                    CredentialKey = AcquisitionHttp.DownloadClientPasswordCredential,
                    CreatedAt = now
                };
                db.DownloadClientCredentials.Add(credential);
            }

            credential.EncryptedValue = command.Password;
            credential.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        var savedPassword = !string.IsNullOrWhiteSpace(command.Password) ? command.Password : await PasswordAsync(row.Id, cancellationToken);
        return ToSummary(ToDetail(row, savedPassword));
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

    private async Task<Dictionary<Guid, string>> PasswordsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) =>
        await db.DownloadClientCredentials
            .AsNoTracking()
            .Where(row => ids.Contains(row.DownloadClientConfigId) && row.CredentialKey == AcquisitionHttp.DownloadClientPasswordCredential)
            .ToDictionaryAsync(row => row.DownloadClientConfigId, row => row.EncryptedValue, cancellationToken);

    private async Task<string?> PasswordAsync(Guid id, CancellationToken cancellationToken) =>
        await db.DownloadClientCredentials
            .AsNoTracking()
            .Where(row => row.DownloadClientConfigId == id && row.CredentialKey == AcquisitionHttp.DownloadClientPasswordCredential)
            .Select(row => row.EncryptedValue)
            .FirstOrDefaultAsync(cancellationToken);

    private static DownloadClientSummary ToSummary(DownloadClientDetail detail) =>
        new(detail.Id, detail.Kind, detail.DisplayName, detail.BaseUrl, detail.Username, detail.Category, detail.Enabled, detail.HasPassword);

    private static DownloadClientDetail ToDetail(DownloadClientConfigRow row, string? password) =>
        new(row.Id, row.Kind, row.DisplayName, row.BaseUrl, row.Username, row.Category, row.Enabled, !string.IsNullOrEmpty(password), password);
}
