using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// PostgreSQL row-lock boundary for a download-client Add. Teardown updates the same acquisition row, so
/// it cannot confirm an empty transfer set and delete the owner between remote acceptance and persistence
/// of the client item id. The in-memory fallback supplies equivalent process-local serialization for tests.
/// </summary>
public sealed class EfAcquisitionTransferAddCoordinator(PrismediaDbContext db)
    : IAcquisitionTransferAddCoordinator {
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> InMemoryLocks = new();

    /// <inheritdoc />
    public async Task<IAcquisitionTransferAddLease?> AcquireAsync(
        Guid acquisitionId,
        CancellationToken cancellationToken) {
        if (!db.Database.IsRelational()) {
            var gate = InMemoryLocks.GetOrAdd(acquisitionId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            if (await db.Acquisitions.AsNoTracking().AnyAsync(
                    row => row.Id == acquisitionId && row.Status == AcquisitionStatus.Queued,
                    cancellationToken)) {
                return new InMemoryLease(gate);
            }

            gate.Release();
            return null;
        }

        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT status FROM acquisitions WHERE id = @id FOR UPDATE";
            var idParameter = command.CreateParameter();
            idParameter.ParameterName = "id";
            idParameter.Value = acquisitionId;
            command.Parameters.Add(idParameter);
            var status = await command.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(
                    Convert.ToString(status, System.Globalization.CultureInfo.InvariantCulture),
                    AcquisitionStatus.Queued.ToCode(),
                    StringComparison.Ordinal)) {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
                return null;
            }

            return new RelationalLease(transaction);
        } catch {
            await transaction.RollbackAsync(CancellationToken.None);
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class RelationalLease(IDbContextTransaction transaction) : IAcquisitionTransferAddLease {
        private bool completed;

        public async Task CommitAsync(CancellationToken cancellationToken) {
            if (completed) {
                return;
            }

            await transaction.CommitAsync(cancellationToken);
            completed = true;
        }

        public async ValueTask DisposeAsync() {
            if (!completed) {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            await transaction.DisposeAsync();
        }
    }

    private sealed class InMemoryLease(SemaphoreSlim gate) : IAcquisitionTransferAddLease {
        private bool disposed;

        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() {
            if (!disposed) {
                disposed = true;
                gate.Release();
            }
            return ValueTask.CompletedTask;
        }
    }
}
