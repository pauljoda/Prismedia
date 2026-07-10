using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>EF-backed authoritative Entity provider-identity binding store.</summary>
public sealed class EfEntityProviderIdentityStore(
    PrismediaDbContext db,
    TimeProvider timeProvider) : IEntityProviderIdentityStore {
    public async Task<EntityProviderIdentityBinding?> GetAsync(
        Guid entityId,
        CancellationToken cancellationToken) {
        var row = await db.EntityProviderIdentities.AsNoTracking()
            .FirstOrDefaultAsync(value => value.EntityId == entityId, cancellationToken);
        if (row is null) {
            return null;
        }

        return new EntityProviderIdentityBinding(
            entityId,
            row.PluginId,
            new ExternalIdentity(row.IdentityNamespace, row.IdentityValue));
    }

    public async Task SetAsync(
        Guid entityId,
        string pluginId,
        ExternalIdentity identity,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(identity);
        var normalizedPluginId = NormalizePluginId(pluginId);
        var identityExists = db.EntityExternalIds.Local.Any(value =>
                value.EntityId == entityId
                && value.Provider == identity.Namespace
                && value.Value == identity.Value
                && db.Entry(value).State != EntityState.Deleted)
            || await db.EntityExternalIds.AnyAsync(value =>
                value.EntityId == entityId
                && value.Provider == identity.Namespace
                && value.Value == identity.Value,
                cancellationToken);
        if (!identityExists) {
            throw new InvalidOperationException(
                "A provider binding must reference an external identity already owned by the Entity.");
        }

        var now = timeProvider.GetUtcNow();
        var row = db.EntityProviderIdentities.Local.FirstOrDefault(value =>
                value.EntityId == entityId && db.Entry(value).State != EntityState.Deleted)
            ?? await db.EntityProviderIdentities
                .FirstOrDefaultAsync(value => value.EntityId == entityId, cancellationToken);
        if (row is null) {
            db.EntityProviderIdentities.Add(new EntityProviderIdentityRow {
                EntityId = entityId,
                PluginId = normalizedPluginId,
                IdentityNamespace = identity.Namespace,
                IdentityValue = identity.Value,
                CreatedAt = now,
                UpdatedAt = now,
            });
            return;
        }

        row.PluginId = normalizedPluginId;
        row.IdentityNamespace = identity.Namespace;
        row.IdentityValue = identity.Value;
        row.UpdatedAt = now;
    }

    private static string NormalizePluginId(string pluginId) {
        if (string.IsNullOrWhiteSpace(pluginId)) {
            throw new ArgumentException("Plugin id cannot be empty.", nameof(pluginId));
        }

        return pluginId.Trim().ToLowerInvariant();
    }
}
