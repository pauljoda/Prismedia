using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Files;

/// <summary>Serializes root configuration with physical writes and external boundary creation across API and worker processes.</summary>
internal static class LibraryRootConfigurationLease {
    /// <summary>Returns a new transaction to commit, or null when the caller already owns the transaction or uses an in-memory test store.</summary>
    internal static async Task<IDbContextTransaction?> AcquireAsync(PrismediaDbContext db, CancellationToken token) {
        if (!db.Database.IsRelational()) return null;
        var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(token) : null;
        try {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({PostgresLibraryFileMutationGuard.ProtectionLockId});", token);
            return transaction;
        } catch {
            if (transaction is not null) await transaction.DisposeAsync();
            throw;
        }
    }
}
