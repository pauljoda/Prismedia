using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Domain.Tests;

public sealed class ManagedHoldingTests {
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void VerifiedObservationFollowsOnlyWhenEveryTargetHasAFile() {
        var holding = ManagedHolding.AwaitFiles(Guid.NewGuid(), Now);

        holding.Reconcile(everyTargetBound: false, Now);
        Assert.Equal(ManagedTrackingStatus.WaitingForFiles, holding.State.Status);

        holding.Reconcile(everyTargetBound: true, Now);
        Assert.Equal(ManagedTrackingStatus.Tracking, holding.State.Status);
        Assert.Null(holding.State.Problem);
        Assert.Equal(Now + ManagedHolding.ObservationInterval, holding.State.NextCheckAt);
        Assert.Equal(3, holding.State.Revision);
    }

    [Fact]
    public void UnverifiableConnectionKeepsAConfirmedRemoval() {
        var followed = ManagedHolding.AwaitFiles(Guid.NewGuid(), Now);
        followed.Reconcile(everyTargetBound: true, Now);
        followed.RecordUnverifiable(Now);
        Assert.Equal(ManagedTrackingStatus.Stale, followed.State.Status);

        var removed = ManagedHolding.AwaitFiles(Guid.NewGuid(), Now);
        removed.ConfirmRemoval("Gone from the manager.", Now);
        removed.RecordUnverifiable(Now);
        Assert.Equal(ManagedTrackingStatus.Removed, removed.State.Status);
        Assert.Contains("last confirmed removal", removed.State.Problem);

        removed.RecordReappearance("The remote identity exists again.", Now);
        Assert.Equal(ManagedTrackingStatus.Removed, removed.State.Status);
        Assert.Throws<InvalidOperationException>(() => followed.RecordReappearance("Unexpected.", Now));
    }

    [Fact]
    public void ReleaseFreezesHostActionsUntilTheHandoffCompletes() {
        var holding = ManagedHolding.AwaitFiles(Guid.NewGuid(), Now);
        var operationId = Guid.NewGuid();

        holding.BeginRelease(operationId, Now);
        Assert.Throws<InvalidOperationException>(() => holding.Reconcile(everyTargetBound: true, Now));
        holding.RecordReleaseProblem("The manager is still importing.", Now);
        Assert.Equal(Now + ManagedHolding.ManagerWaitInterval, holding.State.NextCheckAt);

        holding.CompleteRelease(Now);
        Assert.Equal(ManagedTrackingStatus.Released, holding.State.Status);
        Assert.Equal(Now, holding.State.ReleasedAt);
        Assert.Equal(operationId, holding.State.ReleaseOperationId);
        Assert.Throws<ArgumentException>(() => new ManagedHolding(holding.State with { ReleasedAt = null }));
    }
}
