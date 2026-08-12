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
/// of the client item id. A transaction-scoped advisory lock also serializes correlation in one download
/// client category across API and worker processes. The in-memory fallback supplies equivalent process-local
/// serialization for tests.
/// </summary>
public sealed class EfAcquisitionTransferAddCoordinator(PrismediaDbContext db)
    : IAcquisitionTransferAddCoordinator {
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> InMemoryAcquisitionLocks = new();
    private static readonly ConcurrentDictionary<(Guid ClientId, string Category), SemaphoreSlim> InMemoryCorrelationLocks = new();

    /// <inheritdoc />
    public async Task<IAcquisitionTransferAddLease?> AcquireAsync(
        Guid acquisitionId,
        Guid downloadClientConfigId,
        string category,
        CancellationToken cancellationToken) {
        if (!db.Database.IsRelational()) {
            var acquisitionGate = InMemoryAcquisitionLocks.GetOrAdd(acquisitionId, static _ => new SemaphoreSlim(1, 1));
            await acquisitionGate.WaitAsync(cancellationToken);
            try {
                if (!await db.Acquisitions.AsNoTracking().AnyAsync(
                    row => row.Id == acquisitionId
                        && (row.Status == AcquisitionStatus.Queued
                            || row.Status == AcquisitionStatus.WaitingForDownloadClient),
                    cancellationToken)) {
                    acquisitionGate.Release();
                    return null;
                }

                var correlationGate = InMemoryCorrelationLocks.GetOrAdd(
                    (downloadClientConfigId, category),
                    static _ => new SemaphoreSlim(1, 1));
                await correlationGate.WaitAsync(cancellationToken);
                return new InMemoryLease(acquisitionGate, correlationGate);
            } catch {
                acquisitionGate.Release();
                throw;
            }
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
            var statusCode = Convert.ToString(status, System.Globalization.CultureInfo.InvariantCulture);
            if (!string.Equals(statusCode, AcquisitionStatus.Queued.ToCode(), StringComparison.Ordinal)
                && !string.Equals(
                    statusCode,
                    AcquisitionStatus.WaitingForDownloadClient.ToCode(),
                    StringComparison.Ordinal)) {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
                return null;
            }

            await using var correlationCommand = db.Database.GetDbConnection().CreateCommand();
            correlationCommand.Transaction = transaction.GetDbTransaction();
            correlationCommand.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@correlation_scope, 0))";
            var correlationParameter = correlationCommand.CreateParameter();
            correlationParameter.ParameterName = "correlation_scope";
            correlationParameter.Value = $"download-add:{downloadClientConfigId:N}:{category}";
            correlationCommand.Parameters.Add(correlationParameter);
            await correlationCommand.ExecuteNonQueryAsync(cancellationToken);

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

    private sealed class InMemoryLease(SemaphoreSlim acquisitionGate, SemaphoreSlim correlationGate) : IAcquisitionTransferAddLease {
        private bool disposed;

        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() {
            if (!disposed) {
                disposed = true;
                correlationGate.Release();
                acquisitionGate.Release();
            }
            return ValueTask.CompletedTask;
        }
    }
}
