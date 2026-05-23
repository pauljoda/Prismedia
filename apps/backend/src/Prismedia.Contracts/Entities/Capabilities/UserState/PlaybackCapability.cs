namespace Prismedia.Contracts.Entities;

/// <summary>
/// API-facing playback state capability for time-based media entities.
/// Exposes resume position, play count, and completion state so clients
/// can restore playback where the user left off.
/// </summary>
/// <param name="PlayCount">Number of play sessions recorded.</param>
/// <param name="PlayDurationSeconds">Total accumulated playback duration in seconds.</param>
/// <param name="ResumeSeconds">Position in seconds where playback should resume.</param>
/// <param name="LastPlayedAt">Timestamp of the most recent playback event.</param>
/// <param name="CompletedAt">Timestamp when the entity was fully watched, if applicable.</param>
[CapabilityKind("playback")]
public sealed record PlaybackCapability(
    int PlayCount,
    double PlayDurationSeconds,
    double ResumeSeconds,
    DateTimeOffset? LastPlayedAt,
    DateTimeOffset? CompletedAt) : EntityCapability;
