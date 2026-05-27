using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

internal static class EntityMetadataProposalTraversal {
    public static IReadOnlyList<EntityMetadataProposal> StructuralChildren(EntityMetadataProposal proposal) =>
        (proposal.Children ?? [])
            .Where(child => !IsRelationshipKind(child.TargetKind))
            .ToArray();

    public static IReadOnlyList<EntityMetadataProposal> Relationships(EntityMetadataProposal proposal) {
        var relationships = new List<EntityMetadataProposal>();
        if (proposal.Relationships is { Count: > 0 }) {
            relationships.AddRange(proposal.Relationships);
        }

        relationships.AddRange((proposal.Children ?? []).Where(child => IsRelationshipKind(child.TargetKind)));

        return relationships
            .GroupBy(child => child.ProposalId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool IsRelationshipKind(string kind) =>
        kind is "person" or "studio" or "tag";
}
