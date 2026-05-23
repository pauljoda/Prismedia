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
    /// Applies a sparse playback update from UI or player events.
    /// </summary>
    public void Update(TimeSpan? resumeTime, TimeSpan? duration, bool? completed, DateTimeOffset updatedAt) {
        var nextResume = resumeTime is null
            ? Value.ResumeTime
            : resumeTime.Value < TimeSpan.Zero ? TimeSpan.Zero : resumeTime.Value;
        var addedDuration = duration is null || duration.Value < TimeSpan.Zero
            ? TimeSpan.Zero
            : duration.Value;

        Value = Value with {
            PlayCount = Value.PlayCount == 0 && (resumeTime is not null || duration is not null || completed == true)
                ? 1
                : Value.PlayCount,
            PlayDuration = Value.PlayDuration + addedDuration,
            ResumeTime = nextResume,
            LastPlayedAt = updatedAt,
            CompletedAt = completed switch {
                true => updatedAt,
                false => null,
                _ => Value.CompletedAt
            }
        };
    }
}
