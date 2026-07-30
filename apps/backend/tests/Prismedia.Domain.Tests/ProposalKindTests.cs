using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Tests;

public sealed class ProposalKindTests {
    [Fact]
    public void ProposalKindDerivesEveryEntityKindCode() {
        var entityCodes = Enum.GetValues<EntityKind>().Select(kind => kind.ToCode()).ToHashSet();
        var proposalCodes = CodecRegistry.Get<ProposalKind>().Codes.ToHashSet();

        Assert.Empty(entityCodes.Except(proposalCodes));
    }

    [Fact]
    public void ProposalKindAddsOnlyTheVideoEpisodeToken() {
        var entityCodes = Enum.GetValues<EntityKind>().Select(kind => kind.ToCode()).ToHashSet();
        var proposalExtras = CodecRegistry.Get<ProposalKind>().Codes
            .Where(code => !entityCodes.Contains(code))
            .ToArray();

        Assert.Equal(["video-episode"], proposalExtras);
    }

    [Fact]
    public void EveryEntityKindRoundTripsThroughProposalKind() {
        foreach (var kind in Enum.GetValues<EntityKind>()) {
            Assert.Equal(kind, kind.ToProposalKind().ToEntityKind());
        }
    }

    [Fact]
    public void VideoEpisodeCollapsesToVideo() {
        Assert.Equal(EntityKind.Video, ProposalKind.VideoEpisode.ToEntityKind());
        Assert.NotEqual(EntityKind.Video.ToProposalKind(), ProposalKind.VideoEpisode);
    }

    [Fact]
    public void TaxonomyEntityKindsAreClassifiedAsRelationships() {
        Assert.All(
            new[] { EntityKind.Person, EntityKind.Studio, EntityKind.Tag },
            kind => Assert.True(kind.ToProposalKind().IsRelationship()));
    }

    [Fact]
    public void StructuralAndRootKindsAreNotRelationships() {
        Assert.All(
            new[] {
                EntityKind.Video.ToProposalKind(),
                ProposalKind.VideoEpisode,
                EntityKind.VideoSeason.ToProposalKind(),
                EntityKind.Book.ToProposalKind()
            },
            kind => Assert.False(kind.IsRelationship()));
    }

    [Fact]
    public void CodecRejectsUnknownBlankAndDefaultValues() {
        var codec = CodecRegistry.Get<ProposalKind>();

        Assert.False(codec.TryDecode("unknown", out _));
        Assert.False(codec.TryDecode("  ", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => codec.Encode(default));
    }
}
