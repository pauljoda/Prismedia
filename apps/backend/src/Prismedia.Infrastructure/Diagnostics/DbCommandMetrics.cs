using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Prismedia.Infrastructure.Diagnostics;

/// <summary>
/// Ambient per-request database command metrics. The API's request-timing middleware opens a
/// scope at the start of each request; <see cref="DbCommandCountingInterceptor"/> accumulates
/// executed-command counts and database time into whichever scope is current on the async flow.
/// When no scope is active (worker jobs, startup runners) recording is a no-op, so the
/// interceptor is safe to register on the shared <c>DbContext</c> options for both processes.
/// </summary>
public static class DbCommandMetrics {
    private static readonly AsyncLocal<Scope?> Current = new();

    /// <summary>Opens a new accumulation scope bound to the current async flow.</summary>
    public static Scope Begin() {
        var scope = new Scope();
        Current.Value = scope;
        return scope;
    }

    internal static void Record(TimeSpan duration) => Current.Value?.Record(duration);

    /// <summary>Accumulated command count and total database time for one request.</summary>
    public sealed class Scope {
        private long _commands;
        private long _totalTicks;

        /// <summary>Number of database commands executed while this scope was current.</summary>
        public long Commands => Interlocked.Read(ref _commands);

        /// <summary>Total time spent executing database commands while this scope was current.</summary>
        public TimeSpan TotalTime => TimeSpan.FromTicks(Interlocked.Read(ref _totalTicks));

        internal void Record(TimeSpan duration) {
            Interlocked.Increment(ref _commands);
            Interlocked.Add(ref _totalTicks, duration.Ticks);
        }
    }
}

/// <summary>
/// EF Core command interceptor that feeds <see cref="DbCommandMetrics"/> after every executed
/// command. Stateless; register the shared <see cref="Instance"/> so options caching is stable.
/// </summary>
public sealed class DbCommandCountingInterceptor : DbCommandInterceptor {
    /// <summary>Shared stateless instance.</summary>
    public static readonly DbCommandCountingInterceptor Instance = new();

    private DbCommandCountingInterceptor() { }

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result) {
        DbCommandMetrics.Record(eventData.Duration);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default) {
        DbCommandMetrics.Record(eventData.Duration);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result) {
        DbCommandMetrics.Record(eventData.Duration);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default) {
        DbCommandMetrics.Record(eventData.Duration);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result) {
        DbCommandMetrics.Record(eventData.Duration);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default) {
        DbCommandMetrics.Record(eventData.Duration);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }
}
