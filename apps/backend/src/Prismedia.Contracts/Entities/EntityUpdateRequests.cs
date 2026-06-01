namespace Prismedia.Contracts.Entities;

/// <summary>
/// Request body for setting or clearing an entity rating.
/// </summary>
/// <param name="Value">Rating value from 0 through 5, or null to clear the rating.</param>
public sealed record RatingUpdateRequest(int? Value);

/// <summary>
/// Request body for partially updating shared entity flags.
/// </summary>
/// <param name="IsFavorite">Optional favorite flag value.</param>
/// <param name="IsNsfw">Optional NSFW flag value.</param>
/// <param name="IsOrganized">Optional organized/reviewed flag value.</param>
public sealed record EntityFlagsUpdateRequest(
    bool? IsFavorite,
    bool? IsNsfw,
    bool? IsOrganized);

/// <summary>
/// Request body for recording or updating playback state.
/// All fields are optional — omitted fields leave the existing value unchanged.
/// </summary>
/// <param name="ResumeSeconds">Position in seconds where playback should resume next time.</param>
/// <param name="DurationSeconds">Seconds of playback to add to the total accumulated duration.</param>
/// <param name="Completed">When true, marks the entity as completed; when false, clears completion.</param>
public sealed record PlaybackUpdateRequest(
    double? ResumeSeconds,
    double? DurationSeconds,
    bool? Completed);

/// <summary>
/// Request body for recording non-time progress such as a reading cursor.
/// </summary>
/// <param name="CurrentEntityId">Current child entity, such as a chapter identifier.</param>
/// <param name="Unit">Unit being tracked, such as page or item.</param>
/// <param name="Index">Zero-based position within the tracked unit.</param>
/// <param name="Total">Total number of tracked units available.</param>
/// <param name="Mode">Optional reader/viewing mode associated with the progress.</param>
/// <param name="Completed">When true, marks the progress complete; when false, clears completion in place. Independent of the cursor.</param>
/// <param name="Reset">When true, resets the cursor to the supplied (start) position and clears completion, bypassing the forward-only guard.</param>
/// <param name="Location">Optional format-specific resume locator (e.g. an EPUB CFI) stored alongside the index.</param>
public sealed record EntityProgressUpdateRequest(
    Guid CurrentEntityId,
    string Unit,
    int Index,
    int Total,
    string? Mode,
    bool? Completed,
    bool Reset = false,
    string? Location = null);

/// <summary>
/// Request body for creating or updating a timeline marker.
/// </summary>
/// <param name="Title">Human-readable marker label.</param>
/// <param name="Seconds">Marker start time in seconds.</param>
/// <param name="EndSeconds">Optional marker end time in seconds.</param>
public sealed record EntityMarkerWriteRequest(
    string Title,
    double Seconds,
    double? EndSeconds);
