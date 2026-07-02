using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Requests;

/// <summary>EF-backed discovery blacklist of provider work identities the user removed from Wanted.</summary>
public sealed class EfWantedSuppressionStore(PrismediaDbContext db) : IWantedSuppressionStore {
    public async Task SuppressAsync(IReadOnlyList<ProviderRef> identities, EntityKind kind, string title, CancellationToken cancellationToken) {
        var valid = Normalize(identities);
        if (valid.Count == 0) {
            return;
        }

        var existing = await FilterSuppressedAsync(valid, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var identity in valid) {
            if (existing.Contains(Key(identity))) {
                continue;
            }

            db.WantedSuppressions.Add(new WantedSuppressionRow {
                Id = Guid.NewGuid(),
                Provider = identity.Provider,
                ItemId = identity.ItemId,
                Kind = kind,
                Title = title,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<string>> FilterSuppressedAsync(IReadOnlyList<ProviderRef> identities, CancellationToken cancellationToken) {
        var valid = Normalize(identities);
        if (valid.Count == 0) {
            return new HashSet<string>();
        }

        // Providers repeat across the set, so narrow by provider and finish the pair match in memory.
        var providers = valid.Select(identity => identity.Provider).Distinct().ToArray();
        var rows = await db.WantedSuppressions.AsNoTracking()
            .Where(row => providers.Contains(row.Provider))
            .Select(row => new { row.Provider, row.ItemId })
            .ToArrayAsync(cancellationToken);
        var suppressed = rows.Select(row => $"{row.Provider}:{row.ItemId}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        return valid.Select(Key).Where(suppressed.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task ClearAsync(IReadOnlyList<ProviderRef> identities, CancellationToken cancellationToken) {
        var valid = Normalize(identities);
        if (valid.Count == 0) {
            return;
        }

        var providers = valid.Select(identity => identity.Provider).Distinct().ToArray();
        var rows = await db.WantedSuppressions
            .Where(row => providers.Contains(row.Provider))
            .ToArrayAsync(cancellationToken);
        var keys = valid.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = rows.Where(row => keys.Contains($"{row.Provider}:{row.ItemId}")).ToArray();
        if (matches.Length == 0) {
            return;
        }

        db.WantedSuppressions.RemoveRange(matches);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Key(ProviderRef identity) => $"{identity.Provider}:{identity.ItemId}";

    private static IReadOnlyList<ProviderRef> Normalize(IReadOnlyList<ProviderRef> identities) =>
        identities
            .Where(identity => !string.IsNullOrWhiteSpace(identity.Provider) && !string.IsNullOrWhiteSpace(identity.ItemId))
            .Distinct()
            .ToArray();
}
