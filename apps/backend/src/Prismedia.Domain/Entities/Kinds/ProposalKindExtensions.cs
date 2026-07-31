namespace Prismedia.Domain.Entities;

/// <summary>
/// Maps between the identify-protocol <see cref="ProposalKind"/> vocabulary and the persisted
/// <see cref="EntityKind"/> set, and classifies proposal kinds the apply pipeline treats specially.
/// </summary>
public static class ProposalKindExtensions {
    /// <summary>
    /// Maps a proposal kind to the entity kind Prismedia persists it as. Every proposal kind shares
/// its code with an <see cref="EntityKind"/>.
    /// </summary>
    public static EntityKind ToEntityKind(this ProposalKind kind) => kind.ToPersistedEntityKind();

    /// <summary>
    /// Lifts an entity kind into the proposal vocabulary. Every entity kind maps to the proposal
    /// value with the same persisted kind and code.
    /// </summary>
    public static ProposalKind ToProposalKind(this EntityKind kind) => kind;

    /// <summary>
    /// True when the proposal targets a non-structural related entity (person, studio, or tag)
    /// rather than a structural child or the root entity.
    /// </summary>
    public static bool IsRelationship(this ProposalKind kind) =>
        kind.TryGetEntityKind(out var entityKind) &&
        EntityKindRegistry.Describe(entityKind).Category == EntityKindCategory.Taxonomy;
}
