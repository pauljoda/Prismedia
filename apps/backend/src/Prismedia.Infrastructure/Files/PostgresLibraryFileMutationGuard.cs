using Npgsql;
using Prismedia.Application.Files;
using Prismedia.Contracts.System;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Files;

/// <summary>Holds shared PostgreSQL protection leases across physical writes, with one exclusive lease for mount configuration.</summary>
public sealed class PostgresLibraryFileMutationGuard(NpgsqlDataSource dataSource) : ILibraryFileMutationGuard {
    private const long ProtectionLockId = 5_087_441_904;
    private readonly AsyncLocal<PendingLease?> ambient = new();

    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> EnterAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken) {
        // Assign the holder synchronously in the caller's execution context. Nested async adapters share the
        // same immutable snapshot rather than deadlocking behind an exclusive configuration waiter.
        if (ambient.Value is { Finished: false } current) return new(EnterNestedAsync(current, paths, cancellationToken));
        var pending = new PendingLease();
        ambient.Value = pending;
        return new(EnterOuterAsync(pending, paths, cancellationToken));
    }

    /// <summary>Excludes new filesystem mutations while a mount and its watched root are committed atomically.</summary>
    public async Task<IAsyncDisposable> EnterConfigurationAsync(CancellationToken cancellationToken) =>
        await OpenAsync(exclusive: true, cancellationToken);

    private async Task<IAsyncDisposable> EnterOuterAsync(PendingLease pending, IReadOnlyCollection<string> paths, CancellationToken cancellationToken) {
        DatabaseLease? lease = null;
        try {
            lease = await OpenAsync(exclusive: false, cancellationToken);
            await using var command = new NpgsqlCommand("SELECT local_path FROM external_library_mounts;", lease.Connection, lease.Transaction);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var boundaries = new List<string>();
            while (await reader.ReadAsync(cancellationToken)) {
                var path = Path.GetFullPath(reader.GetString(0));
                boundaries.Add(path);
                boundaries.Add(CompletedPayloadFileSystem.CanonicalPath(path));
            }
            lease.Boundaries = boundaries.Distinct(FileSystemPathComparison.Comparer).ToArray();
            RequireWritable(paths, lease.Boundaries);
            lease.OnDispose = () => pending.Finished = true;
            pending.Ready.TrySetResult(lease);
            return lease;
        } catch (Exception error) {
            pending.Finished = true;
            pending.Ready.TrySetException(error);
            _ = pending.Ready.Task.Exception; // Observe when there was no nested waiter.
            if (lease is not null) await lease.DisposeAsync();
            throw;
        }
    }

    private static async Task<IAsyncDisposable> EnterNestedAsync(PendingLease pending, IReadOnlyCollection<string> paths, CancellationToken cancellationToken) {
        var lease = await pending.Ready.Task.WaitAsync(cancellationToken);
        if (pending.Finished) throw new InvalidOperationException("A filesystem mutation outlived its protection lease.");
        RequireWritable(paths, lease.Boundaries);
        return NestedLease.Instance;
    }

    private static void RequireWritable(IReadOnlyCollection<string> paths, IReadOnlyList<string> boundaries) {
        foreach (var path in paths) {
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Filesystem mutations require absolute paths.");
            var literal = Path.GetFullPath(path);
            var canonical = CompletedPayloadFileSystem.CanonicalPath(path);
            if (boundaries.Any(boundary => CompletedPayloadFileSystem.Overlaps(boundary, literal) || CompletedPayloadFileSystem.Overlaps(boundary, canonical)))
                throw new FileOperationException(ApiProblemCodes.ReadOnlyLibrary, "This path belongs to an externally managed library. Prismedia keeps its files read-only.");
        }
    }

    private async Task<DatabaseLease> OpenAsync(bool exclusive, CancellationToken cancellationToken) {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try {
                await using var command = new NpgsqlCommand(exclusive ? "SELECT pg_advisory_xact_lock(@key);" : "SELECT pg_advisory_xact_lock_shared(@key);", connection, transaction);
                command.Parameters.AddWithValue("key", ProtectionLockId);
                await command.ExecuteNonQueryAsync(cancellationToken);
                return new(connection, transaction);
            } catch { await transaction.DisposeAsync(); throw; }
        } catch { await connection.DisposeAsync(); throw; }
    }

    private sealed class PendingLease {
        internal volatile bool Finished;
        internal TaskCompletionSource<DatabaseLease> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class DatabaseLease(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable {
        private int disposed;
        internal NpgsqlConnection Connection => connection;
        internal NpgsqlTransaction Transaction => transaction;
        internal IReadOnlyList<string> Boundaries { get; set; } = [];
        internal Action? OnDispose { get; set; }
        public async ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            OnDispose?.Invoke();
            try { await transaction.DisposeAsync(); }
            finally { await connection.DisposeAsync(); }
        }
    }
    private sealed class NestedLease : IAsyncDisposable {
        internal static NestedLease Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
