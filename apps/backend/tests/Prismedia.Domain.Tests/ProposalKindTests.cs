using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Tests;

public sealed class ProposalKindTests {
    [Fact]
    public void ProposalKindMirrorsEveryEntityKindCode() {
        var entityCodes = Enum.GetValues<EntityKind>().Select(kind => kind.ToCode()).ToHashSet();
        var proposalCodes = Enum.GetValues<ProposalKind>().Select(kind => kind.ToCode()).ToHashSet();

        // Every entity kind is a valid proposal target, so ProposalKind must cover them all.
        Assert.Empty(entityCodes.Except(proposalCodes));
    }

    [Fact]
    public void ProposalKindAddsOnlyTheVideoEpisodeToken() {
        var entityCodes = Enum.GetValues<EntityKind>().Select(kind => kind.ToCode()).ToHashSet();
        var proposalExtras = Enum.GetValues<ProposalKind>()
            .Select(kind => kind.ToCode())
            .Where(code => !entityCodes.Contains(code))
            .ToArray();

        // The proposal vocabulary is EntityKind plus exactly one provider-only token.
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
    }

    [Theory]
    [InlineData(ProposalKind.Person)]
    [InlineData(ProposalKind.Studio)]
    [InlineData(ProposalKind.Tag)]
    public void RelationshipKindsAreClassifiedAsRelationships(ProposalKind kind) {
        Assert.True(kind.IsRelationship());
    }

    [Theory]
    [InlineData(ProposalKind.Video)]
    [InlineData(ProposalKind.VideoEpisode)]
    [InlineData(ProposalKind.VideoSeason)]
    [InlineData(ProposalKind.Book)]
    public void StructuralAndRootKindsAreNotRelationships(ProposalKind kind) {
        Assert.False(kind.IsRelationship());
    }
}
