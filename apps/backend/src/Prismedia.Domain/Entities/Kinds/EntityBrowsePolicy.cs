namespace Prismedia.Domain.Entities;

/// <summary>
/// Immutable wanted-placeholder filtering contract owned by one Entity kind. Catalog hierarchy
/// visibility belongs to <see cref="EntityCatalogVisibilityPolicy"/>.
/// </summary>
public sealed record EntityBrowsePolicy {
    /// <summary>Policy for a kind that includes wanted placeholders by default.</summary>
    public static EntityBrowsePolicy Default { get; } = new();

    /// <summary>Creates one validated browse policy.</summary>
    /// <param name="excludesWantedByDefault">
    /// Whether wanted placeholders of this kind are omitted unless wanted state is requested.
    /// </param>
    public EntityBrowsePolicy(bool excludesWantedByDefault = false) {
        ExcludesWantedByDefault = excludesWantedByDefault;
    }

    /// <summary>Whether wanted placeholders are omitted unless wanted state is requested.</summary>
    public bool ExcludesWantedByDefault { get; }
}
