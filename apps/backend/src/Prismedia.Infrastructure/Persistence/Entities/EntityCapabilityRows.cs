using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class EntityDescriptionRow {
    public Guid EntityId { get; set; }
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One user's engagement with one entity: playback state (videos/audio), reading
/// progress (books/comics), favorite, and rating — all user opinions, kept apart from
/// the entity's curation facts. One wide row per (user, entity) so shelves, filters,
/// and Jellyfin UserData resolve with a single join.
/// </summary>
public sealed class UserEntityStateRow {
    public Guid UserId { get; set; }
    public Guid EntityId { get; set; }
    public bool IsFavorite { get; set; }
    public int? RatingValue { get; set; }
    public int PlayCount { get; set; }
    public int SkipCount { get; set; }
    public double PlayDurationSeconds { get; set; }
    public double ResumeSeconds { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? ProgressCurrentEntityId { get; set; }
    public string ProgressUnit { get; set; } = "item";
    public int ProgressIndex { get; set; }
    public int ProgressTotal { get; set; }
    public string? ProgressMode { get; set; }
    public string? ProgressLocation { get; set; }
    public DateTimeOffset? ProgressCompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityPlaybackEventRow {
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }

    /// <summary>Null marks pre-multi-user household history without a known owner.</summary>
    public Guid? UserId { get; set; }

    public PlaybackEventKind Kind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public double? PositionSeconds { get; set; }
    public double? DurationSeconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class EntityStatRow {
    public Guid EntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityDateRow {
    public Guid EntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateOnly? SortableValue { get; set; }
    public string? Precision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityTechnicalRow {
    public Guid EntityId { get; set; }
    public double? DurationSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? FrameRate { get; set; }
    public int? BitRate { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public string? Codec { get; set; }
    public string? Container { get; set; }
    public string? Format { get; set; }

    /// <summary>
    /// Set when the most recent probe could not read the source file (corrupt or truncated media).
    /// While set, scans stop re-enqueueing probe and generation work for the entity; the marker is
    /// cleared when the source file changes on disk or a later probe succeeds.
    /// </summary>
    public DateTimeOffset? ProbeFailedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntitySourceRow {
    public Guid EntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityPositionRow {
    public Guid EntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Value { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityClassificationRow {
    public Guid EntityId { get; set; }
    public string? Value { get; set; }
    public string? System { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityLifetimeRow {
    public Guid EntityId { get; set; }
    public string? StartCode { get; set; }
    public string? StartValue { get; set; }
    public DateOnly? StartSortableValue { get; set; }
    public string? StartPrecision { get; set; }
    public string? EndCode { get; set; }
    public string? EndValue { get; set; }
    public DateOnly? EndSortableValue { get; set; }
    public string? EndPrecision { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityFileFingerprintRow {
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public Guid? EntityFileId { get; set; }
    public FingerprintAlgorithm Algorithm { get; set; }
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
