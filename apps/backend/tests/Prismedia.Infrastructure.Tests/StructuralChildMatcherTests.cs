using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class StructuralChildMatcherTests {
    [Fact]
    public void NormalEpisodeMatchStillUsesPositionWhenCountsAgree() {
        var local = Local(EntityKindRegistry.Video.Code, "Different Local Title", 1);
        var provider = Proposal(ProposalKind.VideoEpisode, "Magic Xylophone", ("episodeNumber", 1));

        var match = StructuralChildMatcher.FindProviderChild(local, [provider], new HashSet<int>(), cautious: false);

        Assert.Same(provider, match);
    }

    [Fact]
    public void CountMismatchDoesNotBindBlueyTheSignToProviderSurpriseByEpisodeNumber() {
        var localChildren = Enumerable.Range(1, 48)
            .Select(episode => Local(EntityKindRegistry.Video.Code, $"Episode {episode}", episode))
            .Concat([
                Local(EntityKindRegistry.Video.Code, "The Sign", 49),
                Local(EntityKindRegistry.Video.Code, "Surprise!", 50)
            ])
            .ToArray();
        var providerChildren = Enumerable.Range(1, 48)
            .Select(episode => Proposal(ProposalKind.VideoEpisode, $"Episode {episode}", ("episodeNumber", episode)))
            .Concat([
                Proposal(
                    ProposalKind.VideoEpisode,
                    "Surprise!",
                    ("episodeNumber", 49),
                    new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "4215673" })
            ])
            .ToArray();
        var used = new HashSet<int>();

        foreach (var local in localChildren.Take(48)) {
            Assert.NotNull(StructuralChildMatcher.FindProviderChild(local, providerChildren, used, cautious: true));
        }

        var theSign = StructuralChildMatcher.FindProviderChild(localChildren[48], providerChildren, used, cautious: true);
        var surprise = StructuralChildMatcher.FindProviderChild(localChildren[49], providerChildren, used, cautious: true);

        Assert.Null(theSign);
        Assert.NotNull(surprise);
        Assert.Equal("Surprise!", surprise.Patch.Title);
        Assert.Equal("4215673", surprise.Patch.ExternalIds[ExternalIdProviders.Tmdb]);
    }

    [Fact]
    public void CountMismatchDoesNotBindAlbumTrackWhenNumberMatchesButTitleConflicts() {
        var localTrack = Local(EntityKindRegistry.AudioTrack.Code, "Local Hidden Track", 7);
        var providerTrack = Proposal(ProposalKind.AudioTrack, "Provider Bonus Track", ("trackNumber", 7));

        var match = StructuralChildMatcher.FindProviderChild(
            localTrack,
            [providerTrack],
            new HashSet<int>(),
            cautious: true);

        Assert.Null(match);
    }

    [Fact]
    public void CountMismatchStillBindsNumberMatchWhenTitlesAreCompatibleVariants() {
        var localTrack = Local(EntityKindRegistry.AudioTrack.Code, "Local Episode 1", 1);
        var providerTrack = Proposal(ProposalKind.AudioTrack, "Episode 1", ("trackNumber", 1));

        var match = StructuralChildMatcher.FindProviderChild(
            localTrack,
            [providerTrack],
            new HashSet<int>(),
            cautious: true);

        Assert.Same(providerTrack, match);
    }

    [Fact]
    public void CountMismatchAllowsNumberMatchWhenLocalTitleIsOnlyGenericStructure() {
        var localEpisode = Local(EntityKindRegistry.Video.Code, "Episode 1", 1);
        var providerEpisode = Proposal(ProposalKind.VideoEpisode, "Magic Xylophone", ("episodeNumber", 1));

        var match = StructuralChildMatcher.FindProviderChild(
            localEpisode,
            [providerEpisode],
            new HashSet<int>(),
            cautious: true);

        Assert.Same(providerEpisode, match);
    }

    [Fact]
    public void CountMismatchCanBindAlbumTrackByTitleWhenProviderNumberDiffers() {
        var localTrack = Local(EntityKindRegistry.AudioTrack.Code, "Closer", 12);
        var providerTrack = Proposal(ProposalKind.AudioTrack, "Closer", ("trackNumber", 11));

        var match = StructuralChildMatcher.FindProviderChild(
            localTrack,
            [providerTrack],
            new HashSet<int>(),
            cautious: true);

        Assert.Same(providerTrack, match);
    }

    private static StructuralLocalChild Local(string kindCode, string title, int? sortOrder) =>
        new(Guid.NewGuid(), kindCode, title, sortOrder);

    private static EntityMetadataProposal Proposal(
        ProposalKind kind,
        string title,
        (string Code, int Value) position,
        IReadOnlyDictionary<string, string>? externalIds = null) =>
        new(
            ProposalId: $"{kind.ToCode()}:{position.Value}:{title}",
            Provider: "test-provider",
            TargetKind: kind,
            Confidence: 1m,
            MatchReason: "fixture",
            Patch: new EntityMetadataPatch(
                title,
                Description: null,
                ExternalIds: externalIds ?? new Dictionary<string, string>(),
                Urls: [],
                Tags: [],
                Studio: null,
                Credits: [],
                Dates: new Dictionary<string, string>(),
                Stats: new Dictionary<string, int>(),
                Positions: new Dictionary<string, int> { [position.Code] = position.Value },
                Classification: null),
            Images: [],
            Children: [],
            Candidates: [],
            TargetEntityId: null,
            Relationships: []);
}
