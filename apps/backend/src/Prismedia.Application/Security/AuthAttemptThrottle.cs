using System.Collections.Concurrent;

namespace Prismedia.Application.Security;

/// <summary>Small in-memory throttle for human API key attempts.</summary>
public sealed class AuthAttemptThrottle {
    private const int MaxFailures = 8;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, AttemptState> _attempts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns whether the bucket is currently rate limited.</summary>
    public bool IsThrottled(string bucket) {
        var key = NormalizeBucket(bucket);
        if (!_attempts.TryGetValue(key, out var state)) {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (state.LockedUntil is { } lockedUntil && lockedUntil > now) {
            return true;
        }

        if (now - state.WindowStartedAt > Window) {
            _attempts.TryRemove(key, out _);
        }

        return false;
    }

    /// <summary>Records a failed auth attempt for a bucket.</summary>
    public void RecordFailure(string bucket) {
        var key = NormalizeBucket(bucket);
        var now = DateTimeOffset.UtcNow;
        _attempts.AddOrUpdate(
            key,
            _ => new AttemptState(now, 1, null),
            (_, existing) => {
                var state = now - existing.WindowStartedAt > Window
                    ? new AttemptState(now, 0, null)
                    : existing;
                var failures = state.Failures + 1;
                return state with {
                    Failures = failures,
                    LockedUntil = failures >= MaxFailures ? now + Lockout : state.LockedUntil
                };
            });
    }

    /// <summary>Clears recorded failures after a successful auth attempt.</summary>
    public void RecordSuccess(string bucket) => _attempts.TryRemove(NormalizeBucket(bucket), out _);

    private static string NormalizeBucket(string bucket) =>
        string.IsNullOrWhiteSpace(bucket) ? "unknown" : bucket.Trim().ToLowerInvariant();

    private sealed record AttemptState(DateTimeOffset WindowStartedAt, int Failures, DateTimeOffset? LockedUntil);
}
