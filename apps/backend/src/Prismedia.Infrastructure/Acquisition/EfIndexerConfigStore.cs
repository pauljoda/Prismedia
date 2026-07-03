using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>EF-backed store for indexer configurations, keeping API keys in the credential table out of summaries.</summary>
public sealed class EfIndexerConfigStore(PrismediaDbContext db) : IIndexerConfigStore {
    public async Task<IReadOnlyList<IndexerConfigSummary>> ListAsync(CancellationToken cancellationToken) {
        var details = await ListDetailsAsync(cancellationToken);
        // Health rides the summary so the settings list can flag a backed-off indexer at a glance.
        var statuses = await db.IndexerStatuses.AsNoTracking().ToDictionaryAsync(row => row.IndexerConfigId, cancellationToken);
        return details.Select(detail => {
            var status = statuses.GetValueOrDefault(detail.Id);
            return ToSummary(detail) with {
                DisabledUntil = status?.DisabledUntil is { } until && until > DateTimeOffset.UtcNow ? until : null,
                LastFailureMessage = status?.LastFailureMessage
            };
        }).ToArray();
    }

    public async Task<IReadOnlyList<IndexerConfigDetail>> ListDetailsAsync(CancellationToken cancellationToken) {
        var rows = await db.IndexerConfigs
            .AsNoTracking()
            .OrderBy(row => row.Priority)
            .ThenBy(row => row.DisplayName)
            .ToArrayAsync(cancellationToken);
        var ids = rows.Select(row => row.Id).ToArray();
        var credentials = await db.IndexerCredentials
            .AsNoTracking()
            .Where(row => ids.Contains(row.IndexerConfigId) && row.CredentialKey == AcquisitionHttp.IndexerApiKeyCredential)
            .ToDictionaryAsync(row => row.IndexerConfigId, row => row.EncryptedValue, cancellationToken);

        return rows.Select(row => ToDetail(row, credentials.GetValueOrDefault(row.Id))).ToArray();
    }

    public async Task<IndexerConfigDetail?> GetAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.IndexerConfigs.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null) {
            return null;
        }

        var apiKey = await db.IndexerCredentials
            .AsNoTracking()
            .Where(credential => credential.IndexerConfigId == id && credential.CredentialKey == AcquisitionHttp.IndexerApiKeyCredential)
            .Select(credential => credential.EncryptedValue)
            .FirstOrDefaultAsync(cancellationToken);
        return ToDetail(row, apiKey);
    }

    public async Task<IndexerConfigSummary> SaveAsync(IndexerConfigSaveCommand command, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = command.Id is { } id
            ? await db.IndexerConfigs.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            : null;

        if (row is null) {
            row = new IndexerConfigRow {
                Id = command.Id ?? Guid.NewGuid(),
                CreatedAt = now
            };
            db.IndexerConfigs.Add(row);
        }

        row.Kind = command.Kind;
        row.DisplayName = command.DisplayName;
        row.BaseUrl = command.BaseUrl;
        row.Enabled = command.Enabled;
        row.Priority = command.Priority;
        row.Categories = command.Categories.ToArray();
        row.QueryLimitPerHour = command.QueryLimitPerHour is > 0 ? command.QueryLimitPerHour : null;
        row.SeedRatio = command.SeedRatio is > 0 ? command.SeedRatio : null;
        row.SeedTimeMinutes = command.SeedTimeMinutes is > 0 ? command.SeedTimeMinutes : null;
        row.UpdatedAt = now;

        if (!string.IsNullOrWhiteSpace(command.ApiKey)) {
            var credential = await db.IndexerCredentials.FirstOrDefaultAsync(candidate =>
                candidate.IndexerConfigId == row.Id && candidate.CredentialKey == AcquisitionHttp.IndexerApiKeyCredential,
                cancellationToken);
            if (credential is null) {
                credential = new IndexerCredentialRow {
                    Id = Guid.NewGuid(),
                    IndexerConfigId = row.Id,
                    CredentialKey = AcquisitionHttp.IndexerApiKeyCredential,
                    CreatedAt = now
                };
                db.IndexerCredentials.Add(credential);
            }

            credential.EncryptedValue = command.ApiKey;
            credential.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        var savedApiKey = !string.IsNullOrWhiteSpace(command.ApiKey)
            ? command.ApiKey
            : await db.IndexerCredentials
                .AsNoTracking()
                .Where(credential => credential.IndexerConfigId == row.Id && credential.CredentialKey == AcquisitionHttp.IndexerApiKeyCredential)
                .Select(credential => credential.EncryptedValue)
                .FirstOrDefaultAsync(cancellationToken);
        return ToSummary(ToDetail(row, savedApiKey));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.IndexerConfigs.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null) {
            return false;
        }

        db.IndexerConfigs.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IndexerConfigSummary ToSummary(IndexerConfigDetail detail) =>
        new(detail.Id, detail.Kind, detail.DisplayName, detail.BaseUrl, detail.Enabled, detail.Priority, detail.Categories, detail.HasApiKey, detail.QueryLimitPerHour,
            SeedRatio: detail.SeedRatio, SeedTimeMinutes: detail.SeedTimeMinutes);

    private static IndexerConfigDetail ToDetail(IndexerConfigRow row, string? apiKey) =>
        new(row.Id, row.Kind, row.DisplayName, row.BaseUrl, row.Enabled, row.Priority, row.Categories, !string.IsNullOrEmpty(apiKey), apiKey, row.QueryLimitPerHour, row.SeedRatio, row.SeedTimeMinutes);
}
