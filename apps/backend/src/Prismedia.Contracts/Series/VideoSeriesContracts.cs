using Prismedia.Contracts.Entities;

namespace Prismedia.Contracts.Series;

/// <summary>
/// API-facing video-series detail shape with projected child videos.
/// </summary>
public sealed record VideoSeriesDetail : EntityDetail {
    /// <summary>Relationship edge metadata for credited people shown on detail pages.</summary>
    public required IReadOnlyList<EntityCreditMetadata> CreditMetadata { get; init; }
}

/// <summary>
/// API-facing video-season detail shape with ordered episode videos.
/// </summary>
public sealed record VideoSeasonDetail : EntityDetail {
    /// <summary>Relationship edge metadata for credited people shown on detail pages.</summary>
    public required IReadOnlyList<EntityCreditMetadata> CreditMetadata { get; init; }
}
