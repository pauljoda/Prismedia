using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Requests;

public sealed class EfRequestServiceInstanceStore(PrismediaDbContext db) : IRequestServiceInstanceStore {
    public async Task<IReadOnlyList<RequestServiceInstanceSummary>> ListAsync(CancellationToken cancellationToken) {
        var details = await ListDetailsAsync(cancellationToken);
        return details.Select(ToSummary).ToArray();
    }

    public async Task<IReadOnlyList<RequestServiceInstanceDetail>> ListDetailsAsync(CancellationToken cancellationToken) {
        var rows = await db.RequestServiceInstances
            .AsNoTracking()
            .OrderBy(row => row.Kind)
            .ThenBy(row => row.DisplayName)
            .ToArrayAsync(cancellationToken);
        var ids = rows.Select(row => row.Id).ToArray();
        var credentials = await db.RequestServiceCredentials
            .AsNoTracking()
            .Where(row => ids.Contains(row.ServiceInstanceId) && row.CredentialKey == RequestProviderHttp.ApiKeyCredential)
            .ToDictionaryAsync(row => row.ServiceInstanceId, row => row.EncryptedValue, cancellationToken);

        return rows.Select(row => ToDetail(row, credentials.GetValueOrDefault(row.Id))).ToArray();
    }

    public async Task<RequestServiceInstanceDetail?> GetAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.RequestServiceInstances.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null) {
            return null;
        }

        var apiKey = await db.RequestServiceCredentials
            .AsNoTracking()
            .Where(credential => credential.ServiceInstanceId == id && credential.CredentialKey == RequestProviderHttp.ApiKeyCredential)
            .Select(credential => credential.EncryptedValue)
            .FirstOrDefaultAsync(cancellationToken);
        return ToDetail(row, apiKey);
    }

    public async Task<RequestServiceInstanceSummary> SaveAsync(RequestServiceInstanceSaveCommand command, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = command.Id is { } id
            ? await db.RequestServiceInstances.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            : null;

        if (row is null) {
            row = new RequestServiceInstanceRow {
                Id = command.Id ?? Guid.NewGuid(),
                CreatedAt = now
            };
            db.RequestServiceInstances.Add(row);
        }

        var hasAnyOfKind = await db.RequestServiceInstances.AnyAsync(candidate =>
            candidate.Kind == command.Kind && candidate.Id != row.Id, cancellationToken);
        var shouldBeDefault = command.IsDefault || !hasAnyOfKind;

        row.Kind = command.Kind;
        row.DisplayName = command.DisplayName;
        row.BaseUrl = command.BaseUrl;
        row.DefaultRootFolderPath = command.DefaultRootFolderPath;
        row.DefaultQualityProfileId = command.DefaultQualityProfileId;
        row.DefaultMetadataProfileId = command.DefaultMetadataProfileId;
        row.MinimumAvailability = command.MinimumAvailability;
        row.DefaultTagIds = command.DefaultTagIds.ToArray();
        row.SearchOnRequest = command.SearchOnRequest;
        row.IsDefault = shouldBeDefault;
        row.UpdatedAt = now;

        if (shouldBeDefault) {
            var priorDefaults = await db.RequestServiceInstances
                .Where(candidate => candidate.Kind == command.Kind && candidate.Id != row.Id)
                .ToArrayAsync(cancellationToken);
            foreach (var priorDefault in priorDefaults) {
                priorDefault.IsDefault = false;
            }
        }

        if (!string.IsNullOrWhiteSpace(command.ApiKey)) {
            var credential = await db.RequestServiceCredentials.FirstOrDefaultAsync(candidate =>
                candidate.ServiceInstanceId == row.Id && candidate.CredentialKey == RequestProviderHttp.ApiKeyCredential,
                cancellationToken);
            if (credential is null) {
                credential = new RequestServiceCredentialRow {
                    Id = Guid.NewGuid(),
                    ServiceInstanceId = row.Id,
                    CredentialKey = RequestProviderHttp.ApiKeyCredential,
                    CreatedAt = now
                };
                db.RequestServiceCredentials.Add(credential);
            }

            credential.EncryptedValue = command.ApiKey;
            credential.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        var savedApiKey = !string.IsNullOrWhiteSpace(command.ApiKey)
            ? command.ApiKey
            : await db.RequestServiceCredentials
                .AsNoTracking()
                .Where(credential => credential.ServiceInstanceId == row.Id && credential.CredentialKey == RequestProviderHttp.ApiKeyCredential)
                .Select(credential => credential.EncryptedValue)
                .FirstOrDefaultAsync(cancellationToken);
        return ToSummary(ToDetail(row, savedApiKey));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.RequestServiceInstances.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null) {
            return false;
        }

        var kind = row.Kind;
        var wasDefault = row.IsDefault;
        var replacement = wasDefault
            ? await db.RequestServiceInstances
                .Where(candidate => candidate.Kind == kind && candidate.Id != id)
                .OrderBy(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        if (replacement is not null) {
            replacement.IsDefault = true;
        }

        db.RequestServiceInstances.Remove(row);
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static RequestServiceInstanceSummary ToSummary(RequestServiceInstanceDetail detail) =>
        new(detail.Id, detail.Kind, detail.DisplayName, detail.BaseUrl, detail.IsDefault, detail.DefaultRootFolderPath,
            detail.DefaultQualityProfileId, detail.DefaultMetadataProfileId, detail.MinimumAvailability, detail.DefaultTagIds,
            detail.SearchOnRequest, detail.HasApiKey);

    private static RequestServiceInstanceDetail ToDetail(RequestServiceInstanceRow row, string? apiKey) =>
        new(row.Id, row.Kind, row.DisplayName, row.BaseUrl, row.IsDefault, row.DefaultRootFolderPath,
            row.DefaultQualityProfileId, row.DefaultMetadataProfileId, row.MinimumAvailability, row.DefaultTagIds,
            row.SearchOnRequest, !string.IsNullOrEmpty(apiKey), apiKey);
}
