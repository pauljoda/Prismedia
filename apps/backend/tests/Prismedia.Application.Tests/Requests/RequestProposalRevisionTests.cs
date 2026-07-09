using Prismedia.Application.Requests;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Requests;

public sealed class RequestProposalRevisionTests {
    [Fact]
    public void SemanticallyEqualDictionaryOrderProducesTheSameRevision() {
        var first = Proposal(
            new Dictionary<string, string> { ["tmdb"] = "123", ["imdb"] = "tt123" },
            new Dictionary<string, string> { ["release"] = "2024", ["digital"] = "2024-03-01" });
        var second = Proposal(
            new Dictionary<string, string> { ["imdb"] = "tt123", ["tmdb"] = "123" },
            new Dictionary<string, string> { ["digital"] = "2024-03-01", ["release"] = "2024" });

        Assert.Equal(RequestProposalRevision.Compute(first), RequestProposalRevision.Compute(second));
    }

    [Fact]
    public void ChangedNestedChildProducesADifferentRevision() {
        var original = Proposal(
            new Dictionary<string, string> { ["tmdb"] = "123" },
            new Dictionary<string, string> { ["release"] = "2024" });
        var changed = original with {
            Children = [original.Children[0] with {
                Patch = original.Children[0].Patch with { Title = "Changed episode" }
            }]
        };

        Assert.NotEqual(RequestProposalRevision.Compute(original), RequestProposalRevision.Compute(changed));
    }

    private static EntityMetadataProposal Proposal(
        IReadOnlyDictionary<string, string> externalIds,
        IReadOnlyDictionary<string, string> dates) =>
        new(
            "root",
            "cinema-metadata",
            ProposalKind.VideoSeries,
            1,
            "external-id",
            new EntityMetadataPatch(
                "Series",
                "Overview",
                externalIds,
                [],
                ["Drama", "Mystery"],
                null,
                [],
                dates,
                new Dictionary<string, int> { ["runtimeMinutes"] = 45 },
                new Dictionary<string, int>(),
                null),
            [],
            [
                new EntityMetadataProposal(
                    "child",
                    "cinema-metadata",
                    ProposalKind.VideoEpisode,
                    1,
                    "cascade",
                    new EntityMetadataPatch(
                        "Episode",
                        null,
                        new Dictionary<string, string> { ["tmdb"] = "episode:1" },
                        [],
                        [],
                        null,
                        [],
                        new Dictionary<string, string>(),
                        new Dictionary<string, int>(),
                        new Dictionary<string, int> { ["episode"] = 1 },
                        null),
                    [],
                    [],
                    [],
                    null,
                    [])
            ],
            [],
            null,
            []);
}
