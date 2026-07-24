using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Plugins;

public sealed class EntityMetadataProposalIdentityPolicyTests {
    [Fact]
    public void RemoveSharedStructuralIdentitiesKeepsOnlyNodeUniqueEvidence() {
        var firstEpisode = Proposal(
            "episode:1",
            ProposalKind.VideoEpisode,
            new Dictionary<string, string> {
                ["series-db"] = "series-42",
                ["season-db"] = "season-1",
                ["episode-db"] = "episode-1"
            });
        var secondEpisode = Proposal(
            "episode:2",
            ProposalKind.VideoEpisode,
            new Dictionary<string, string> {
                ["series-db"] = "series-42",
                ["season-db"] = "season-1",
                ["episode-db"] = "episode-2"
            });
        var firstSeason = Proposal(
            "season:1",
            ProposalKind.VideoSeason,
            new Dictionary<string, string> {
                ["series-db"] = "series-42",
                ["season-db"] = "season-1"
            },
            [firstEpisode, secondEpisode]);
        var secondSeason = Proposal(
            "season:2",
            ProposalKind.VideoSeason,
            new Dictionary<string, string> {
                ["series-db"] = "series-42",
                ["season-db"] = "season-2"
            });
        var sharedPerson = Proposal(
            "person:1",
            ProposalKind.Person,
            new Dictionary<string, string> { ["person-db"] = "person-1" });
        var root = Proposal(
            "series:42",
            ProposalKind.VideoSeries,
            new Dictionary<string, string> { ["series-db"] = "series-42" },
            [firstSeason, secondSeason],
            [sharedPerson, sharedPerson with { ProposalId = "person:1:again" }]);

        var sanitized = EntityMetadataProposalIdentityPolicy.RemoveSharedStructuralIdentities(root);

        Assert.Equal("series-42", sanitized.Patch.ExternalIds["series-db"]);
        Assert.Equal(
            new Dictionary<string, string> { ["season-db"] = "season-1" },
            sanitized.Children[0].Patch.ExternalIds);
        Assert.Equal(
            new Dictionary<string, string> { ["season-db"] = "season-2" },
            sanitized.Children[1].Patch.ExternalIds);
        Assert.Equal(
            new Dictionary<string, string> { ["episode-db"] = "episode-1" },
            sanitized.Children[0].Children[0].Patch.ExternalIds);
        Assert.Equal(
            new Dictionary<string, string> { ["episode-db"] = "episode-2" },
            sanitized.Children[0].Children[1].Patch.ExternalIds);
        Assert.All(
            sanitized.Relationships,
            relationship => Assert.Equal("person-1", relationship.Patch.ExternalIds["person-db"]));
    }

    [Fact]
    public void RemoveSharedStructuralIdentitiesIgnoresInvalidLocatorValues() {
        var child = Proposal(
            "season:1",
            ProposalKind.VideoSeason,
            new Dictionary<string, string> {
                ["season-db"] = "season-1",
                ["source"] = "https://example.test/season/1"
            });

        var sanitized = EntityMetadataProposalIdentityPolicy.RemoveSharedStructuralIdentities(
            Proposal("series:1", ProposalKind.VideoSeries, new Dictionary<string, string>(), [child]));

        Assert.Equal(child.Patch.ExternalIds, sanitized.Children[0].Patch.ExternalIds);
    }

    [Fact]
    public void RemoveSharedStructuralIdentitiesAllowsMissingIdentityMaps() {
        var firstSeason = Proposal(
            "season:1",
            ProposalKind.VideoSeason,
            new Dictionary<string, string> { ["series-db"] = "series-1" });
        var secondSeason = Proposal(
            "season:2",
            ProposalKind.VideoSeason,
            new Dictionary<string, string> { ["series-db"] = "series-1" });
        var root = Proposal(
            "series:1",
            ProposalKind.VideoSeries,
            new Dictionary<string, string>(),
            [firstSeason, secondSeason]);
        root = root with { Patch = root.Patch with { ExternalIds = null! } };

        var sanitized = EntityMetadataProposalIdentityPolicy.RemoveSharedStructuralIdentities(root);

        Assert.Null(sanitized.Patch.ExternalIds);
        Assert.All(sanitized.Children, child => Assert.Empty(child.Patch.ExternalIds));
    }

    private static EntityMetadataProposal Proposal(
        string proposalId,
        ProposalKind kind,
        IReadOnlyDictionary<string, string> externalIds,
        IReadOnlyList<EntityMetadataProposal>? children = null,
        IReadOnlyList<EntityMetadataProposal>? relationships = null) =>
        new(
            proposalId,
            "fixture",
            kind,
            1,
            "fixture",
            new EntityMetadataPatch(
                proposalId,
                null,
                externalIds,
                [],
                [],
                null,
                [],
                new Dictionary<string, string>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                null),
            [],
            children ?? [],
            [],
            null,
            relationships ?? []);
}
