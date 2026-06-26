using Prismedia.Contracts.Entities;

namespace Prismedia.Contracts.Media;

/// <summary>
/// API-facing detail shape for a book author grouping. Its books arrive as the <c>book</c> child group,
/// mirroring how a music artist's albums arrive as the <c>audio-library</c> child group.
/// </summary>
public sealed record BookAuthorDetail : EntityDetail {
    /// <summary>Relationship edge metadata for credited people (e.g. co-authors, illustrators).</summary>
    public required IReadOnlyList<EntityCreditMetadata> CreditMetadata { get; init; }
}
