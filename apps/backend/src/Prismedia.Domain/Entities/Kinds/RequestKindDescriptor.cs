namespace Prismedia.Domain.Entities;

/// <summary>
/// Describes one request entry owned by an Entity kind definition: how it is presented, what
/// plugins are asked for, what Entity a commit creates, and how structural children behave.
/// A definition may expose more than one request entry when several renditions resolve to the
/// same Entity kind, as Book does for ebooks and audiobooks.
/// </summary>
/// <param name="Kind">Stable request-media identity.</param>
/// <param name="Label">Singular user-facing name.</param>
/// <param name="Plural">Plural user-facing name.</param>
/// <param name="ChildNoun">User-facing noun for selectable direct children.</param>
/// <param name="PluginEntityKind">Entity kind plugins are asked to resolve.</param>
/// <param name="WantedEntityKind">Entity kind materialized as the wanted placeholder.</param>
/// <param name="ProfileEntityKind">Acquisition-profile kind governing downloads.</param>
/// <param name="ReviewSelection">How a proposal maps to selectable commit targets.</param>
/// <param name="IsContainer">Whether the request groups selectable works.</param>
/// <param name="ChildKind">Request identity of exposed child options.</param>
/// <param name="Committable">Whether this request entry can currently be committed.</param>
/// <param name="AcquisitionKind">Entity kind stamped on acquisitions for leaves.</param>
/// <param name="Discoverable">Whether Discover offers the entry directly.</param>
/// <param name="AcquireFromEntity">Whether acquisition context must come from the Entity graph.</param>
/// <param name="MaterializeChildPhantoms">Whether acquisition hydrates structural child placeholders.</param>
/// <param name="DeferChildPhantomHydration">Whether selected child expansion runs after commit.</param>
/// <param name="BookRendition">Requested book rendition, or null for non-book entries.</param>
/// <param name="IsDefaultEntityRequest">Whether this descriptor handles an Entity request when no specific rendition is selected.</param>
public sealed record RequestKindDescriptor(
    RequestMediaKind Kind,
    string Label,
    string Plural,
    string? ChildNoun,
    EntityKind PluginEntityKind,
    EntityKind WantedEntityKind,
    EntityKind? ProfileEntityKind,
    RequestReviewSelection ReviewSelection,
    bool IsContainer,
    RequestMediaKind? ChildKind,
    bool Committable,
    EntityKind AcquisitionKind,
    bool Discoverable = true,
    bool AcquireFromEntity = false,
    bool MaterializeChildPhantoms = false,
    bool DeferChildPhantomHydration = false,
    BookRendition? BookRendition = null,
    bool IsDefaultEntityRequest = true) {
    /// <summary>The plugin-protocol code for <see cref="PluginEntityKind"/>.</summary>
    public string PluginKindCode => PluginEntityKind.ToCode();
}
