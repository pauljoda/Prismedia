namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable playback capability for time-based entities.
/// </summary>
public sealed class CapabilityPlayback : EntityCapability {
    /// <summary>
    /// Creates a playback capability.
    /// </summary>
    /// <param name="value">Initial playback state.</param>
    public CapabilityPlayback(State? value = null) {
        Value = value ?? State.Empty;
    }

    /// <summary>
    /// Single-user playback state for a time-based media entity.
    /// </summary>
    /// <param name="PlayCount">Number of completed or started play sessions recorded for the entity.</param>
    /// <param name="PlayDuration">Total accumulated playback duration.</param>
    /// <param name="ResumeTime">Position where playback should resume.</param>
    /// <param name="LastPlayedAt">Timestamp of the most recent playback event.</param>
    /// <param name="CompletedAt">Timestamp when the entity was completed, when applicable.</param>
    public sealed record State(
        int PlayCount,
        TimeSpan PlayDuration,
        TimeSpan ResumeTime,
        DateTimeOffset? LastPlayedAt,
        DateTimeOffset? CompletedAt) {
        /// <summary>Empty playback state for media that has never been played.</summary>
        public static State Empty { get; } = new(0, TimeSpan.Zero, TimeSpan.Zero, null, null);
    }

    /// <summary>Single-user playback state.</summary>
    public State Value { get; private set; }

    /// <summary>
    /// Records a playback event.
    /// </summary>
    /// <param name="resumeTime">Position where the next session should resume.</param>
    /// <param name="playedAt">Timestamp of the playback event.</param>
    public void MarkPlayed(TimeSpan resumeTime, DateTimeOffset playedAt) {
        Value = Value with {
            PlayCount = Value.PlayCount + 1,
            ResumeTime = resumeTime < TimeSpan.Zero ? TimeSpan.Zero : resumeTime,
            LastPlayedAt = playedAt,
            CompletedAt = null
        };
    }

    /// <summary>
    /// Records an in-progress position so the next session can resume there. Leaves the
    /// completion timestamp and play count untouched; this is the mid-watch progress signal.
    /// </summary>
    /// <param name="position">Position where the next session should resume.</param>
    /// <param name="at">Timestamp of the playback event.</param>
    public void RecordResume(TimeSpan position, DateTimeOffset at) {
        Value = Value with {
            ResumeTime = position < TimeSpan.Zero ? TimeSpan.Zero : position,
            LastPlayedAt = at
        };
    }

    /// <summary>
    /// Records that playback has effectively restarted from the beginning: clears the resume
    /// position and re-arms completion (so a fresh watch-through can be counted again) without
    /// affecting the existing play count.
    /// </summary>
    /// <param name="at">Timestamp of the playback event.</param>
    public void RecordStartOver(DateTimeOffset at) {
        Value = Value with {
            ResumeTime = TimeSpan.Zero,
            CompletedAt = null,
            LastPlayedAt = at
        };
    }

    /// <summary>
    /// Records that playback reached the completion threshold. Increments the play count and
    /// stamps the completion time only on the transition from not-completed to completed, so
    /// repeated end-of-stream signals within a single session are idempotent. Always clears the
    /// resume position so a completed item does not re-open mid-stream.
    /// </summary>
    /// <param name="at">Timestamp of the completion event.</param>
    public void RecordCompleted(DateTimeOffset at) {
        Value = Value with {
            PlayCount = Value.CompletedAt is null ? Value.PlayCount + 1 : Value.PlayCount,
            ResumeTime = TimeSpan.Zero,
            CompletedAt = at,
            LastPlayedAt = at
        };
    }

    /// <summary>
    /// Records a discrete completed playback event from a player that only reports once at end of
    /// stream. Unlike threshold progress, each call represents a completed session and advances the
    /// play count even when the entity was already marked completed.
    /// </summary>
    /// <param name="at">Timestamp of the completed play event.</param>
    public void RecordCompletedPlay(DateTimeOffset at) {
        Value = Value with {
            PlayCount = Value.PlayCount + 1,
            ResumeTime = TimeSpan.Zero,
            CompletedAt = at,
            LastPlayedAt = at
        };
    }

    /// <summary>
    /// Explicitly marks the entity watched without disturbing the resume position. Increments the
    /// play count only on the transition from not-watched to watched. This is the manual
    /// watched-toggle path, kept independent of playback position by design.
    /// </summary>
    /// <param name="at">Timestamp of the event.</param>
    public void MarkWatched(DateTimeOffset at) {
        Value = Value with {
            PlayCount = Value.CompletedAt is null ? Value.PlayCount + 1 : Value.PlayCount,
            CompletedAt = at,
            LastPlayedAt = at
        };
    }

    /// <summary>
    /// Explicitly marks the entity not watched without disturbing the resume position or play
    /// count. This is the manual watched-toggle path, kept independent of playback position.
    /// </summary>
    /// <param name="at">Timestamp of the event.</param>
    public void MarkUnwatched(DateTimeOffset at) {
        Value = Value with {
            CompletedAt = null,
            LastPlayedAt = at
        };
    }

    /// <summary>
    /// Accumulates additional watched duration into the running total.
    /// </summary>
    /// <param name="delta">Amount of playback time to add; non-positive values are ignored.</param>
    public void AccumulatePlayDuration(TimeSpan delta) {
        if (delta <= TimeSpan.Zero) {
            return;
        }

        Value = Value with { PlayDuration = Value.PlayDuration + delta };
    }
}
