using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Applies an acquisition import hint to a freshly scanned book. Matches the hint to the entity by path
/// containment (the scanned book path and the hint's import path overlap), then writes the plugin/external
/// ids onto the entity so the existing identify hint resolver runs ID-first. Consuming the hint keeps it
/// from re-applying on later rescans.
/// </summary>
public sealed class AcquisitionHintApplier(PrismediaDbContext db) : IAcquisitionHintApplier {
    public async Task<bool> ApplyAsync(Guid entityId, string sourcePath, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(sourcePath)) {
            return false;
        }

        var normalized = Normalize(sourcePath);
        var hints = await db.AcquisitionImportHints
            .Where(hint => !hint.Consumed)
            .ToArrayAsync(cancellationToken);

        // Most specific match wins: prefer the longest hint path that overlaps the scanned book path.
        var match = hints
            .Where(hint => PathsOverlap(normalized, Normalize(hint.SourcePath)))
            .OrderByDescending(hint => hint.SourcePath.Length)
            .FirstOrDefault();
        if (match is null) {
            return false;
        }

        var externalIds = DecodeExternalIds(match);
        if (externalIds.Count > 0) {
            var existing = await db.EntityExternalIds
                .Where(row => row.EntityId == entityId)
                .Select(row => row.Provider)
                .ToArrayAsync(cancellationToken);
            var existingProviders = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

            var now = DateTimeOffset.UtcNow;
            foreach (var (provider, value) in externalIds) {
                if (existingProviders.Contains(provider)) {
                    continue;
                }

                db.EntityExternalIds.Add(new EntityExternalIdRow {
                    Id = Guid.NewGuid(),
                    EntityId = entityId,
                    Provider = provider,
                    Value = value,
                    Url = null,
                    CreatedAt = now
                });
            }
        }

        // Record the owned source tier on the book's detail row (the format tier is derived from the row's
        // Format, never stored). This is the provenance half of the owned quality the upgrade loop compares
        // against. The scan creates the detail row before hints are applied, so it is expected to exist.
        var detail = await db.BookDetails.FirstOrDefaultAsync(row => row.EntityId == entityId, cancellationToken);
        if (detail is not null) {
            detail.SourceTier = match.OwnedSourceTier;
        }

        // NOTE: we deliberately do NOT seed the entity's description from the request here. The book's
        // description is owned by the more authoritative sources that run at/after import — the file's own
        // embedded metadata (e.g. ComicInfo) and the post-import auto-identify pass — and seeding here (before
        // the embedded-metadata step) would pre-empt the file's own description. The request-time description
        // is held on the acquisition for the request surface; the imported entity gets the better source.

        match.Consumed = true;
        match.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static Dictionary<string, string> DecodeExternalIds(AcquisitionImportHintRow hint) {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(hint.ExternalIdsJson)) {
            var decoded = JsonSerializer.Deserialize<Dictionary<string, string>>(hint.ExternalIdsJson);
            if (decoded is not null) {
                foreach (var (provider, value) in decoded) {
                    if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(value)) {
                        ids[provider] = value;
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(hint.PluginId) && !string.IsNullOrWhiteSpace(hint.PluginItemId)) {
            ids[hint.PluginId] = hint.PluginItemId;
        }

        return ids;
    }

    private static bool PathsOverlap(string a, string b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase)
        || a.StartsWith(b + "/", StringComparison.OrdinalIgnoreCase)
        || b.StartsWith(a + "/", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimEnd('/');
}
