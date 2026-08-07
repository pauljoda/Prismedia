using System.Data;
using Microsoft.Extensions.Logging;
using Npgsql;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Processes;

/// <summary>
/// PostgreSQL-backed media-process leases shared by the API and worker runtimes in a Prismedia deployment.
/// </summary>
public sealed class PostgresMediaProcessLeaseStore : IMediaProcessLeaseStore {
    private const long BackgroundAdmissionLockId = 5_087_441_903;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PlaybackRegistrationBudget = TimeSpan.FromMilliseconds(250);
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _ownerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly ILogger<PostgresMediaProcessLeaseStore> _logger;

    /// <summary>Creates a shared lease store using the application's PostgreSQL data source.</summary>
    public PostgresMediaProcessLeaseStore(
        NpgsqlDataSource dataSource,
        ILogger<PostgresMediaProcessLeaseStore> logger) {
        _dataSource = dataSource;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> RegisterPlaybackAsync(CancellationToken cancellationToken) {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(PlaybackRegistrationBudget);
        var id = Guid.NewGuid();
        await InsertLeaseAsync(id, MediaProcessKind.Playback, budget.Token);
        return new DatabaseLease(this, id);
    }

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable?> TryAcquireBackgroundAsync(
        int maxConcurrent,
        CancellationToken cancellationToken) {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        await using (var admissionLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@lock_id);",
            connection,
            transaction)) {
            admissionLock.Parameters.AddWithValue("lock_id", BackgroundAdmissionLockId);
            await admissionLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await DeleteExpiredAsync(connection, transaction, cancellationToken);

        var playbackCode = MediaProcessKind.Playback.ToCode();
        var backgroundCode = MediaProcessKind.Background.ToCode();
        var hasCapacity = false;
        await using (var capacity = new NpgsqlCommand(
            """
            SELECT
                COUNT(*) FILTER (WHERE kind = @playback_kind),
                COUNT(*) FILTER (WHERE kind = @background_kind)
            FROM media_process_leases
            WHERE expires_at > now();
            """,
            connection,
            transaction)) {
            capacity.Parameters.AddWithValue("playback_kind", playbackCode);
            capacity.Parameters.AddWithValue("background_kind", backgroundCode);
            await using var reader = await capacity.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var playbackCount = reader.GetInt64(0);
            var backgroundCount = reader.GetInt64(1);
            hasCapacity = playbackCount == 0 && backgroundCount < Math.Max(1, maxConcurrent);
        }
        if (!hasCapacity) {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var id = Guid.NewGuid();
        await InsertLeaseAsync(id, MediaProcessKind.Background, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DatabaseLease(this, id);
    }

    private async Task InsertLeaseAsync(
        Guid id,
        MediaProcessKind kind,
        CancellationToken cancellationToken) {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await InsertLeaseAsync(id, kind, connection, transaction: null, cancellationToken);
    }

    private async Task InsertLeaseAsync(
        Guid id,
        MediaProcessKind kind,
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken) {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO media_process_leases
                (id, kind, owner_id, acquired_at, heartbeat_at, expires_at)
            VALUES
                (@id, @kind, @owner_id, now(), now(), now() + @lease_duration);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("kind", kind.ToCode());
        command.Parameters.AddWithValue("owner_id", _ownerId);
        command.Parameters.AddWithValue("lease_duration", LeaseDuration);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteExpiredAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken) {
        await using var cleanup = new NpgsqlCommand(
            "DELETE FROM media_process_leases WHERE expires_at <= now();",
            connection,
            transaction);
        await cleanup.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task HeartbeatAsync(Guid id, CancellationToken cancellationToken) {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE media_process_leases
            SET heartbeat_at = now(), expires_at = now() + @lease_duration
            WHERE id = @id;
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("lease_duration", LeaseDuration);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ReleaseAsync(Guid id) {
        try {
            await using var connection = await _dataSource.OpenConnectionAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand(
                "DELETE FROM media_process_leases WHERE id = @id;",
                connection);
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Could not release media process lease {LeaseId}; it will expire automatically.", id);
        }
    }

    private sealed class DatabaseLease : IAsyncDisposable {
        private readonly PostgresMediaProcessLeaseStore _store;
        private readonly Guid _id;
        private readonly CancellationTokenSource _heartbeatCancellation = new();
        private readonly Task _heartbeat;
        private int _disposed;

        internal DatabaseLease(PostgresMediaProcessLeaseStore store, Guid id) {
            _store = store;
            _id = id;
            _heartbeat = HeartbeatLoopAsync();
        }

        public async ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) {
                return;
            }

            await _heartbeatCancellation.CancelAsync();
            try {
                await _heartbeat;
            } catch (OperationCanceledException) {
                // Normal lease disposal interrupts the heartbeat delay.
            }

            _heartbeatCancellation.Dispose();
            await _store.ReleaseAsync(_id);
        }

        private async Task HeartbeatLoopAsync() {
            while (!_heartbeatCancellation.IsCancellationRequested) {
                await Task.Delay(HeartbeatInterval, _heartbeatCancellation.Token);
                try {
                    await _store.HeartbeatAsync(_id, _heartbeatCancellation.Token);
                } catch (OperationCanceledException) when (_heartbeatCancellation.IsCancellationRequested) {
                    return;
                } catch (Exception ex) {
                    _store._logger.LogWarning(ex,
                        "Could not heartbeat media process lease {LeaseId}; retrying before expiry.",
                        _id);
                }
            }
        }
    }
}
