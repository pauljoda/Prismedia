using Prismedia.Contracts.Entities;

namespace Prismedia.Contracts.Collections;

/// <summary>
/// API-facing collection detail shape with expanded collection members.
/// </summary>
public sealed record CollectionDetail : EntityDetail {
    /// <summary>Collection membership mode.</summary>
    public required string? Mode { get; init; }

    /// <summary>Dynamic collection rule tree JSON, when present.</summary>
    public required string? RuleTreeJson { get; init; }

    /// <summary>Collection cover selection mode.</summary>
    public required string? CoverMode { get; init; }

    /// <summary>Entity selected as the collection cover item.</summary>
    public required Guid? CoverItemId { get; init; }

    /// <summary>Duration for collection slideshow advancement.</summary>
    public required TimeSpan? SlideshowDuration { get; init; }

    /// <summary>Whether collection slideshows advance automatically.</summary>
    public required bool? SlideshowAutoAdvance { get; init; }

    /// <summary>Last dynamic refresh timestamp, when known.</summary>
    public required DateTimeOffset? LastRefreshedAt { get; init; }
}
