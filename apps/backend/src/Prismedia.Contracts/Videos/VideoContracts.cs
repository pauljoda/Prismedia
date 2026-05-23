using Prismedia.Contracts.Entities;

namespace Prismedia.Contracts.Videos;

/// <summary>
/// API-facing video detail shape combining video metadata with shared entity capabilities.
/// </summary>
public sealed record VideoDetail : EntityDetail {
    /// <summary>Relationship edge metadata for credited people shown on detail pages.</summary>
    public required IReadOnlyList<EntityCreditMetadata> CreditMetadata { get; init; }

    /// <summary>When embedded subtitles were last extracted, when known.</summary>
    public required DateTimeOffset? SubtitlesExtractedAt { get; init; }
}
