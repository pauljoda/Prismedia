using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Domain.Tests;

public sealed class ManagedSourceReconciliationTests {
    private static readonly ManagedTargetIdentity Episode = new("episode-1", EntityKind.VideoEpisode, 1, 2, null);
    private static readonly DateTimeOffset WrittenAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static ManagedFileBinding Binding(params ManagedTargetIdentity[] targets) => new("old-file", "/library/old.mkv", 100, WrittenAt, true,
        targets.Select(target => new ManagedEntityBinding(target, Guid.NewGuid(), Guid.NewGuid())).ToArray());
    private static ManagedObservedFile Observed(params ManagedTargetIdentity[] targets) => new("new-file", "/library/new.mkv", 200, WrittenAt.AddDays(1), true, targets);

    [Fact]
    public void ReplacementPreservesTheExactExistingEntityAndFileBindings() {
        var binding = Binding(Episode);
        var result = ManagedSourceReconciliation.Plan([binding], [Observed(Episode)]);
        Assert.Null(result.ReviewReason);
        var change = Assert.Single(result.Changes);
        Assert.Same(binding, change.Previous);
        Assert.Equal("new-file", change.Current!.RemoteFileId);
    }

    [Fact]
    public void UnchangedObservationDoesNotInvalidateTheExistingSource() {
        var binding = Binding(Episode);
        var observed = new ManagedObservedFile(binding.RemoteFileId, binding.LocalPath, binding.SizeBytes, binding.WrittenAt, true, [Episode]);
        var result = ManagedSourceReconciliation.Plan([binding], [observed]);
        Assert.Null(result.ReviewReason);
        Assert.Empty(result.Changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingRemoteOrUnreadableLocalFileRemovesAvailabilityWithoutDroppingIdentity(bool presentRemotely) {
        var binding = Binding(Episode);
        var result = ManagedSourceReconciliation.Plan([binding], presentRemotely ? [Observed(Episode) with { IsReadable = false }] : []);
        Assert.Null(result.ReviewReason);
        var change = Assert.Single(result.Changes);
        Assert.Null(change.Current);
        Assert.Equal(binding.Entities[0].EntityId, change.Previous.Entities[0].EntityId);
    }

    [Fact]
    public void ChangedEpisodeCoordinatesRequireReviewBeforeAnySourcesChange() {
        var result = ManagedSourceReconciliation.Plan([Binding(Episode)], [Observed(Episode with { EpisodeNumber = 3 })]);
        Assert.NotNull(result.ReviewReason);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void SplittingASharedFileRequiresReviewRatherThanRebindingAllOwnersToOneHalf() {
        var second = Episode with { RemoteTargetId = "episode-2", EpisodeNumber = 3 };
        var result = ManagedSourceReconciliation.Plan([Binding(Episode, second)], [Observed(Episode), Observed(second) with { RemoteFileId = "split-second", LocalPath = "/library/second.mkv" }]);
        Assert.NotNull(result.ReviewReason);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void ReplacingSharedFileWithSameCoverageRetainsEveryOwner() {
        var second = Episode with { RemoteTargetId = "episode-2", EpisodeNumber = 3 };
        var result = ManagedSourceReconciliation.Plan([Binding(Episode, second)], [Observed(second, Episode)]);
        Assert.Null(result.ReviewReason);
        Assert.Equal(2, Assert.Single(result.Changes).Previous.Entities.Count);
    }

    [Fact]
    public void ReusedTargetAcrossSeveralObservedFilesIsAmbiguous() {
        var result = ManagedSourceReconciliation.Plan([Binding(Episode)], [Observed(Episode), Observed(Episode) with { RemoteFileId = "duplicate" }]);
        Assert.NotNull(result.ReviewReason);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void SeparateUnownedTargetsDoNotExpandOrInvalidateTheFiniteTrackedScope() {
        var result = ManagedSourceReconciliation.Plan([Binding(Episode)], [Observed(Episode), Observed(Episode with { RemoteTargetId = "new-episode", EpisodeNumber = 4 }) with { RemoteFileId = "new-episode-file" }]);
        Assert.Null(result.ReviewReason);
        Assert.Single(result.Changes);
    }

    [Fact]
    public void UnownedTargetSharingOwnedBytesRequiresReview() {
        var unowned = Episode with { RemoteTargetId = "new-episode", EpisodeNumber = 4 };
        var result = ManagedSourceReconciliation.Plan([Binding(Episode)], [Observed(Episode, unowned)]);
        Assert.NotNull(result.ReviewReason);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void ChangedRemoteIdAtOwnedCoordinatesRequiresReview() {
        var changedIdentity = Episode with { RemoteTargetId = "replacement-id" };
        var result = ManagedSourceReconciliation.Plan([Binding(Episode)], [Observed(changedIdentity)]);
        Assert.NotNull(result.ReviewReason);
        Assert.Empty(result.Changes);
    }
}
