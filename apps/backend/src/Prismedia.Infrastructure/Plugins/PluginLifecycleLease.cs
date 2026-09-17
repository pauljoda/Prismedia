using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>Serializes package activation/removal with new durable work and connection configuration across API and worker processes.</summary>
internal static class PluginLifecycleLease {
    private const int LockNamespace = 0x50524D50;

    /// <summary>Returns a transaction owned by the caller, or null if the caller already has one or uses an in-memory store.</summary>
    internal static async Task<IDbContextTransaction?> AcquireAsync(PrismediaDbContext db, string pluginId, CancellationToken token) {
        if (!db.Database.IsRelational()) return null;
        var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(token) : null;
        try {
            var key = pluginId.ToLowerInvariant();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockNamespace}, hashtext({key}));", token);
            return transaction;
        } catch {
            if (transaction is not null) await transaction.DisposeAsync();
            throw;
        }
    }

    /// <summary>Joins an acceptance transaction before adding work which would prevent a package change.</summary>
    internal static async Task LockConnectionAsync(PrismediaDbContext db, Guid connectionId, CancellationToken token, bool requireReady = false) {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Plugin work acceptance requires a transaction.");
        var pluginId = await db.IntegrationConnections.AsNoTracking().Where(row => row.Id == connectionId).Select(row => row.PluginId).SingleOrDefaultAsync(token);
        if (pluginId is not null) await AcquireAsync(db, pluginId, token);
        if (requireReady && pluginId is not null && !await db.IntegrationConnections.AsNoTracking()
            .AnyAsync(row => row.Id == connectionId && row.Enabled && row.Status == ConnectionStatus.Ready, token))
            throw new ConnectionConflictException();
    }
}
