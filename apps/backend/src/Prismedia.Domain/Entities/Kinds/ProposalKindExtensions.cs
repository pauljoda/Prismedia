namespace Prismedia.Domain.Entities;

/// <summary>
/// Maps between the identify-protocol <see cref="ProposalKind"/> vocabulary and the persisted
/// <see cref="EntityKind"/> set, and classifies proposal kinds the apply pipeline treats specially.
/// </summary>
public static class ProposalKindExtensions {
    /// <summary>
    /// Maps a proposal kind to the entity kind Prismedia persists it as. Every proposal kind shares
    /// its code with an <see cref="EntityKind"/> except <see cref="ProposalKind.VideoEpisode"/>,
    /// which collapses to <see cref="EntityKind.Video"/>.
    /// </summary>
    public static EntityKind ToEntityKind(this ProposalKind kind) =>
        kind == ProposalKind.VideoEpisode
            ? EntityKind.Video
            : kind.ToCode().DecodeAs<EntityKind>();

    /// <summary>
    /// Lifts an entity kind into the proposal vocabulary. The mapping is identity by code; no entity
    /// kind maps to <see cref="ProposalKind.VideoEpisode"/> (that token is provider-only).
    /// </summary>
    public static ProposalKind ToProposalKind(this EntityKind kind) =>
        kind.ToCode().DecodeAs<ProposalKind>();

    /// <summary>
    /// True when the proposal targets a non-structural related entity (person, studio, or tag)
    /// rather than a structural child or the root entity.
    /// </summary>
    public static bool IsRelationship(this ProposalKind kind) =>
        kind is ProposalKind.Person or ProposalKind.Studio or ProposalKind.Tag;
}
