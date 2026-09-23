using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Domain.Tests;

public sealed class ManagedSourceAdoptionTests {
    private static readonly ManagedTargetIdentity Episode = new("7", EntityKind.VideoEpisode, 0, 1, null);
    private static readonly Guid Entity = Guid.NewGuid();
    private static readonly Guid File = Guid.NewGuid();
    private static ManagedObservedFile Observed(params ManagedTargetIdentity[] targets) => new("3", "/library/episode.mkv", 100,
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"), true, targets);
    private static ManagedLocalSource Owner() => new(Entity, File, "/library/episode.mkv", EntityKind.VideoEpisode, 0, 1, null);

    [Fact]
    public void ExplicitExactMatchPreservesExistingEntityAndFileIdsIncludingSpecials() {
        var result = ManagedSourceAdoption.Plan([Observed(Episode)], [new("7", Entity, File)], [Owner()]);
        Assert.Null(result.ReviewReason);
        var binding = Assert.Single(result.Bindings);
        Assert.Equal(new ManagedEntityBinding(Episode, Entity, File), Assert.Single(binding.Entities));
    }

    [Fact]
    public void ChangedCoordinatesDoNotSilentlyLinkEvenAnExplicitSelection() {
        var result = ManagedSourceAdoption.Plan([Observed(Episode with { SeasonNumber = 1 })], [new("7", Entity, File)], [Owner()]);
        Assert.NotNull(result.ReviewReason);
        Assert.Empty(result.Bindings);
    }

    [Theory]
    [InlineData("12.5", "12.5", true)]
    [InlineData("12.5", "12", false)]
    [InlineData("12.5", null, false)]
    [InlineData(null, "12.5", false)]
    public void ComicIssueAssociationRequiresTheExactDesignation(string? remoteLabel, string? localLabel, bool accepted) {
        var target = new ManagedTargetIdentity("issue-1", EntityKind.ComicInstallment, null, null, null, remoteLabel);
        var owner = new ManagedLocalSource(Entity, File, "/library/episode.mkv", EntityKind.ComicInstallment,
            null, null, null, localLabel);

        var plan = ManagedSourceAdoption.Plan([Observed(target)], [new("issue-1", Entity, File)], [owner]);

        Assert.Equal(accepted, plan.ReviewReason is null);
    }

    [Fact]
    public void EverySharedFileOwnerMustBeSelectedWithExactCoverage() {
        var owner2 = Owner() with { EntityId = Guid.NewGuid(), SourceFileId = Guid.NewGuid(), EpisodeNumber = 2 };
        var target2 = Episode with { RemoteTargetId = "8", EpisodeNumber = 2 };
        Assert.NotNull(ManagedSourceAdoption.Plan([Observed(Episode)], [new("7", Entity, File)], [Owner(), owner2]).ReviewReason);
        Assert.NotNull(ManagedSourceAdoption.Plan([Observed(Episode, target2)], [new("7", Entity, File)], [Owner(), owner2]).ReviewReason);
        Assert.Null(ManagedSourceAdoption.Plan([Observed(Episode, target2)],
            [new("7", Entity, File), new("8", owner2.EntityId, owner2.SourceFileId)], [Owner(), owner2]).ReviewReason);
    }

    [Fact]
    public void UnreadableOrDuplicateSourcesCannotBecomeAvailableBindings() {
        Assert.NotNull(ManagedSourceAdoption.Plan([Observed(Episode) with { IsReadable = false }], [new("7", Entity, File)], [Owner()]).ReviewReason);
        Assert.NotNull(ManagedSourceAdoption.Plan([Observed(Episode), Observed(Episode)], [new("7", Entity, File)], [Owner()]).ReviewReason);
    }
}
