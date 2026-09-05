using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>One client-wide lock shared by remote adds and destructive removals across API and worker processes.</summary>
internal static class DownloadClientOperationLock {
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();
    private static readonly ConditionalWeakTable<PrismediaDbContext, Dictionary<Guid, int>> Held = new();

    /// <summary>Acquires a client lease; relational leases remain held until the caller's transaction ends.</summary>
    public static async Task<IAsyncDisposable> AcquireAsync(PrismediaDbContext db, Guid clientId, CancellationToken token) {
        if (!db.Database.IsRelational()) {
            var held = Held.GetOrCreateValue(db);
            var gate = Gates.GetOrAdd(clientId, static _ => new SemaphoreSlim(1, 1));
            if (held.TryGetValue(clientId, out var count)) {
                held[clientId] = count + 1;
                return new MemoryLease(gate, held, clientId);
            }
            await gate.WaitAsync(token);
            held[clientId] = 1;
            return new MemoryLease(gate, held, clientId);
        }
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Download client operations require an active transaction.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@scope, 0))";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "scope";
        parameter.Value = $"download-client-operation:{clientId:N}";
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(token);
        return TransactionLease.Instance;
    }

    // EF contexts are not shared concurrently. Reentrancy covers compensation inside the same Add
    // lease, matching PostgreSQL's transaction-scoped advisory lock behavior.
    private sealed class MemoryLease(SemaphoreSlim gate, Dictionary<Guid, int> held, Guid clientId) : IAsyncDisposable {
        public ValueTask DisposeAsync() {
            if (--held[clientId] == 0) { held.Remove(clientId); gate.Release(); }
            return ValueTask.CompletedTask;
        }
    }
    private sealed class TransactionLease : IAsyncDisposable {
        public static readonly TransactionLease Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
