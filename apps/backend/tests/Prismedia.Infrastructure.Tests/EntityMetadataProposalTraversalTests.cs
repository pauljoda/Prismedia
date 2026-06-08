using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class EntityMetadataProposalTraversalTests {
    [Fact]
    public void StructuralChildrenExcludeRelationshipMetadataKinds() {
        var proposal = Proposal("root", "video", children: [
            Proposal("season-1", "video-season"),
            Proposal("person-1", "person"),
            Proposal("studio-1", "studio"),
            Proposal("tag-1", "tag")
        ]);

        var children = EntityMetadataProposalTraversal.StructuralChildren(proposal);

        Assert.Single(children);
        Assert.Equal("season-1", children[0].ProposalId);
    }

    [Fact]
    public void RelationshipsUseExplicitRelationshipPayloadOnly() {
        var duplicateRelationship = Proposal("person-1", "person");
        var proposal = Proposal(
            "root",
            "video",
            children: [
                duplicateRelationship,
                Proposal("season-1", "video-season")
            ],
            relationships: [
                duplicateRelationship,
                duplicateRelationship,
                Proposal("studio-1", "studio")
            ]);

        var relationships = EntityMetadataProposalTraversal.Relationships(proposal);

        Assert.Equal(["person-1", "studio-1"], relationships.Select(item => item.ProposalId).ToArray());
    }

    private static EntityMetadataProposal Proposal(
        string id,
        string targetKind,
        IReadOnlyList<EntityMetadataProposal>? children = null,
        IReadOnlyList<EntityMetadataProposal>? relationships = null) =>
        new(
            ProposalId: id,
            Provider: "test",
            TargetKind: targetKind.DecodeAs<ProposalKind>(),
            Confidence: null,
            MatchReason: null,
            Patch: new EntityMetadataPatch(
                Title: id,
                Description: null,
                ExternalIds: new Dictionary<string, string>(),
                Urls: [],
                Tags: [],
                Studio: null,
                Credits: [],
                Dates: new Dictionary<string, string>(),
                Stats: new Dictionary<string, int>(),
                Positions: new Dictionary<string, int>(),
                Classification: null),
            Images: [],
            Children: children ?? [],
            Candidates: [],
            Relationships: relationships ?? []);
}
