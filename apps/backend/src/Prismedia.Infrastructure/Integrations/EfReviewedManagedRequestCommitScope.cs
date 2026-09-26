using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Shares the scoped EF unit of work across wanted materialization and managed-request acceptance.</summary>
public sealed class EfReviewedManagedRequestCommitScope(PrismediaDbContext db) : IReviewedManagedRequestCommitScope {
    #region Actions - Transactions

    /// <inheritdoc />
    public async Task<T> ExecuteAsync<T>(
        Guid connectionId,
        long expectedConnectionRevision,
        Func<CancellationToken, Task<T>> action,
        CancellationToken token) {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null) {
            return await ExecuteLockedAsync(connectionId, expectedConnectionRevision, action, token);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(token);
        try {
            var result = await ExecuteLockedAsync(connectionId, expectedConnectionRevision, action, token);
            await transaction.CommitAsync(token);
            return result;
        } catch {
            await transaction.RollbackAsync(token);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<T> ExecuteLockedAsync<T>(
        Guid connectionId,
        long expectedConnectionRevision,
        Func<CancellationToken, Task<T>> action,
        CancellationToken token) {
        await PluginLifecycleLease.LockConnectionAsync(db, connectionId, token, requireReady: true);
        var revision = await db.IntegrationConnections.AsNoTracking()
            .Where(connection => connection.Id == connectionId)
            .Select(connection => (long?)connection.Revision)
            .SingleOrDefaultAsync(token);
        if (revision != expectedConnectionRevision) {
            throw new ConnectionConflictException(
                "The selected manager connection changed. Review its options again.");
        }

        return await action(token);
    }

    #endregion
}
