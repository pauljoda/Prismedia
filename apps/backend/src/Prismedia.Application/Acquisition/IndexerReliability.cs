using System.Collections.Concurrent;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// The escalating backoff ladder for a failing indexer (mirrors Prowlarr's): each consecutive
/// failure climbs one level, each success steps one level back down, so a flapping indexer is
/// suppressed for progressively longer windows instead of polluting every search — and recovers
/// gradually instead of all at once.
/// </summary>
public static class IndexerBackoffLadder {
    private static readonly TimeSpan[] Levels = [
        TimeSpan.Zero,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(24)
    ];

    /// <summary>The suppression window for the given escalation level (clamped to the ladder).</summary>
    public static TimeSpan For(int level) => Levels[Math.Clamp(level, 0, Levels.Length - 1)];

    /// <summary>The highest escalation level; failures beyond this stay at the 24h ceiling.</summary>
    public static int MaxLevel => Levels.Length - 1;
}

/// <summary>An indexer's current health: its escalation level and the suppression window it is inside, if any.</summary>
public sealed record IndexerHealth(Guid IndexerConfigId, int EscalationLevel, DateTimeOffset? DisabledUntil, string? LastFailureMessage) {
    /// <summary>True when the indexer is inside its suppression window and should be skipped.</summary>
    public bool IsDisabledAt(DateTimeOffset now) => DisabledUntil is { } until && until > now;
}

/// <summary>Persistence port for per-indexer health (escalating failure backoff).</summary>
public interface IIndexerStatusStore {
    /// <summary>The health of every indexer that has recorded at least one failure. Absent indexers are healthy.</summary>
    Task<IReadOnlyDictionary<Guid, IndexerHealth>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Climbs the indexer one escalation level and opens the matching suppression window.</summary>
    Task RecordFailureAsync(Guid indexerConfigId, string message, CancellationToken cancellationToken);

    /// <summary>Steps the indexer one escalation level back down (clearing the window at level zero).</summary>
    Task RecordSuccessAsync(Guid indexerConfigId, CancellationToken cancellationToken);
}

/// <summary>
/// In-memory sliding-window query limiter, keyed by indexer. Deliberately process-local: a restart
/// forgets the window, which errs on the permissive side rather than persisting bookkeeping rows for
/// every search. An indexer with no configured limit is never gated.
/// </summary>
public sealed class IndexerQueryWindow {
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> _queries = new();

    /// <summary>
    /// Records the query when the indexer is under its hourly limit and returns true; returns false —
    /// recording nothing — when the window is already full and the query should be skipped.
    /// </summary>
    public bool TryRecordQuery(Guid indexerConfigId, int? queryLimitPerHour) {
        if (queryLimitPerHour is not { } limit || limit <= 0) {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var queue = _queries.GetOrAdd(indexerConfigId, _ => new Queue<DateTimeOffset>());
        lock (queue) {
            while (queue.Count > 0 && now - queue.Peek() > Window) {
                queue.Dequeue();
            }

            if (queue.Count >= limit) {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }
}
