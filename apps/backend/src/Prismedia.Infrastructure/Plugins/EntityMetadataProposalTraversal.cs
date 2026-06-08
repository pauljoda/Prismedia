using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

internal static class EntityMetadataProposalTraversal {
    public static IReadOnlyList<EntityMetadataProposal> StructuralChildren(EntityMetadataProposal proposal) =>
        (proposal.Children ?? [])
            .Where(child => !IsRelationshipKind(child.TargetKind))
            .ToArray();

    public static IReadOnlyList<EntityMetadataProposal> Relationships(EntityMetadataProposal proposal) =>
        (proposal.Relationships ?? [])
            .GroupBy(child => child.ProposalId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

    public static bool IsRelationshipKind(ProposalKind kind) =>
        kind.IsRelationship();
}
