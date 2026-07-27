using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Queue;

/// <summary>
/// Serializes mutations of one durable graph through its PostgreSQL row lock. The scope joins an
/// existing transaction when graph expansion and signal resolution already share a unit of work.
/// </summary>
internal sealed class JobGraphMutationScope : IAsyncDisposable {
    private readonly IDbContextTransaction? _ownedTransaction;

    private JobGraphMutationScope(JobGraphRow graph, IDbContextTransaction? ownedTransaction) {
        Graph = graph;
        _ownedTransaction = ownedTransaction;
    }

    public JobGraphRow Graph { get; }

    public static async Task<JobGraphMutationScope?> AcquireAsync(
        PrismediaDbContext db,
        Guid graphId,
        CancellationToken cancellationToken) {
        IDbContextTransaction? transaction = null;
        try {
            if (db.Database.IsRelational() && db.Database.CurrentTransaction is null) {
                transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            }

            var tracked = db.ChangeTracker.Entries<JobGraphRow>()
                .FirstOrDefault(entry => entry.Entity.Id == graphId);
            if (tracked?.State == EntityState.Modified) {
                throw new InvalidOperationException(
                    $"Graph '{graphId}' has an unsaved mutation before its scheduling lock was acquired.");
            }

            JobGraphRow? graph;
            if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true) {
                if (tracked is null) {
                    graph = await db.JobGraphs
                        .FromSqlInterpolated($"SELECT *, xmin FROM job_graphs WHERE id = {graphId} FOR UPDATE")
                        .SingleOrDefaultAsync(cancellationToken);
                } else {
                    var lockedId = await db.JobGraphs
                        .FromSqlInterpolated($"SELECT *, xmin FROM job_graphs WHERE id = {graphId} FOR UPDATE")
                        .AsNoTracking()
                        .Select(candidate => (Guid?)candidate.Id)
                        .SingleOrDefaultAsync(cancellationToken);
                    if (lockedId is null) {
                        graph = null;
                    } else {
                        await tracked.ReloadAsync(cancellationToken);
                        graph = tracked.Entity;
                    }
                }
            } else {
                if (tracked is not null) {
                    await tracked.ReloadAsync(cancellationToken);
                    graph = tracked.Entity;
                } else {
                    graph = await db.JobGraphs.SingleOrDefaultAsync(
                        candidate => candidate.Id == graphId,
                        cancellationToken);
                }
            }

            if (graph is not null) {
                return new JobGraphMutationScope(graph, transaction);
            }

            if (transaction is not null) {
                await transaction.RollbackAsync(cancellationToken);
                await transaction.DisposeAsync();
            }
            return null;
        } catch {
            if (transaction is not null) {
                await transaction.DisposeAsync();
            }
            throw;
        }
    }

    public Task CommitAsync(CancellationToken cancellationToken) =>
        _ownedTransaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync() {
        if (_ownedTransaction is not null) {
            await _ownedTransaction.DisposeAsync();
        }
    }
}
