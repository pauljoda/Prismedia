namespace Prismedia.Domain.Entities;

/// <summary>
/// Global-search exposure owned by an Entity-kind definition. A null search contract means the
/// kind is intentionally omitted from the shared search surface.
/// </summary>
public sealed record EntityKindSearch {
    /// <summary>Creates one validated search contract.</summary>
    /// <param name="order">Stable display order in global-search filters and result sections.</param>
    /// <param name="expandsRelationshipResults">
    /// Whether matching this kind should hydrate entities related to the direct result.
    /// </param>
    public EntityKindSearch(int order, bool expandsRelationshipResults = false) {
        if (order < 0) {
            throw new ArgumentOutOfRangeException(nameof(order), "Search order cannot be negative.");
        }

        Order = order;
        ExpandsRelationshipResults = expandsRelationshipResults;
    }

    /// <summary>Stable display order in global-search filters and result sections.</summary>
    public int Order { get; }

    /// <summary>Whether direct matches should hydrate their related entities.</summary>
    public bool ExpandsRelationshipResults { get; }
}
