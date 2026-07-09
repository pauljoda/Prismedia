using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Requests;

/// <summary>EF-backed discovery blacklist of provider work identities the user removed from Wanted.</summary>
public sealed class EfWantedSuppressionStore(PrismediaDbContext db) : IWantedSuppressionStore {
    public async Task SuppressAsync(
        IReadOnlyList<ExternalIdentity> identities,
        EntityKind kind,
        string title,
        CancellationToken cancellationToken) {
        var valid = DistinctIdentities(identities);
        if (valid.Count == 0) {
            return;
        }

        var existing = await FilterSuppressedAsync(valid, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var identity in valid) {
            if (existing.Contains(identity)) {
                continue;
            }

            db.WantedSuppressions.Add(new WantedSuppressionRow {
                Id = Guid.NewGuid(),
                Provider = identity.Namespace,
                ItemId = identity.Value,
                Kind = kind,
                Title = title,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<ExternalIdentity>> FilterSuppressedAsync(
        IReadOnlyList<ExternalIdentity> identities,
        CancellationToken cancellationToken) {
        var valid = DistinctIdentities(identities);
        if (valid.Count == 0) {
            return new HashSet<ExternalIdentity>();
        }

        // Namespaces repeat across the set, so narrow by canonical namespace and finish the identity
        // match in memory. Trim/lower keeps suppressions written before ExternalIdentity canonicalization
        // discoverable without weakening the opaque value's case-sensitive semantics.
        var namespaces = valid.Select(identity => identity.Namespace).Distinct().ToArray();
        var rows = await db.WantedSuppressions.AsNoTracking()
            .Where(row => namespaces.Contains(row.Provider.Trim().ToLower()))
            .Select(row => new { row.Provider, row.ItemId })
            .ToArrayAsync(cancellationToken);
        var suppressed = rows
            .Select(row => TryIdentity(row.Provider, row.ItemId))
            .Where(identity => identity is not null)
            .Select(identity => identity!)
            .ToHashSet();
        return valid.Where(suppressed.Contains).ToHashSet();
    }

    public async Task ClearAsync(
        IReadOnlyList<ExternalIdentity> identities,
        CancellationToken cancellationToken) {
        var valid = DistinctIdentities(identities);
        if (valid.Count == 0) {
            return;
        }

        var namespaces = valid.Select(identity => identity.Namespace).Distinct().ToArray();
        var rows = await db.WantedSuppressions
            .Where(row => namespaces.Contains(row.Provider.Trim().ToLower()))
            .ToArrayAsync(cancellationToken);
        var wanted = valid.ToHashSet();
        var matches = rows
            .Where(row => TryIdentity(row.Provider, row.ItemId) is { } identity && wanted.Contains(identity))
            .ToArray();
        if (matches.Length == 0) {
            return;
        }

        db.WantedSuppressions.RemoveRange(matches);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<ExternalIdentity> DistinctIdentities(IReadOnlyList<ExternalIdentity> identities) {
        ArgumentNullException.ThrowIfNull(identities);
        if (identities.Any(identity => identity is null)) {
            throw new ArgumentException("Wanted suppression identity sets cannot contain null values.", nameof(identities));
        }

        return identities.Distinct().ToArray();
    }

    private static ExternalIdentity? TryIdentity(string identityNamespace, string value) {
        try {
            return new ExternalIdentity(identityNamespace, value);
        } catch (ArgumentException) {
            return null;
        }
    }
}
