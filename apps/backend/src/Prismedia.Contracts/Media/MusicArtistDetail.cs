using Prismedia.Contracts.Entities;

namespace Prismedia.Contracts.Media;

/// <summary>
/// API-facing detail shape for a music artist or band grouping. Its albums arrive as the
/// <c>audio-library</c> child group and its members as person credit relationships, where the
/// credit label carries the member's role (e.g. "Drummer").
/// </summary>
public sealed record MusicArtistDetail : EntityDetail {
    /// <summary>Relationship edge metadata for band members (people credited with a role).</summary>
    public required IReadOnlyList<EntityCreditMetadata> CreditMetadata { get; init; }
}
