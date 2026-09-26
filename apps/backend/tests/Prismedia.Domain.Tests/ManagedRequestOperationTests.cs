using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Domain.Tests;

public sealed class ManagedRequestOperationTests {
    private static ManagedRequestOperation Create() => ManagedRequestOperation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitHandoffStopsWaitingOrCompletedRequestWithoutErasingItsIdentity(bool completed) {
        var operation = Create(); operation.AcceptHolding("42");
        if (completed) operation.ConfirmFiles();
        operation.ReleaseOwnership();
        Assert.Equal(ManagedRequestPhase.OwnershipReleased, operation.State.Phase);
        Assert.Equal("42", operation.State.RemoteId);
        Assert.False(operation.IsActive); Assert.False(operation.CanCancel);
        Assert.Throws<InvalidOperationException>(operation.ConfirmFiles);
    }
    [Fact]
    public void UncertainCreationCannotReleaseOwnershipWithoutAHolding() {
        var operation = Create(); operation.BeginCreation();
        Assert.Throws<InvalidOperationException>(operation.ReleaseOwnership);
    }
    [Fact]
    public void CreationDispatchFenceSurvivesRehydrationAndCannotBeRepeatedOrCancelled() {
        var operation = Create(); operation.BeginCreation();
        var recovered = new ManagedRequestOperation(operation.State);
        Assert.Equal(2, recovered.State.Revision);
        Assert.Throws<InvalidOperationException>(recovered.BeginCreation);
        Assert.Throws<InvalidOperationException>(recovered.Cancel);
        recovered.RequireReview();
        Assert.True(recovered.State.ReviewRequired);
        Assert.Throws<InvalidOperationException>(recovered.BeginCreation);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingOrRecoveredHoldingWaitsForExactLocalFiles(bool dispatched) {
        var operation = Create();
        if (dispatched) operation.BeginCreation();
        operation.AcceptHolding("opaque-remote-id");
        Assert.Equal(ManagedRequestPhase.AwaitingFiles, operation.State.Phase);
        Assert.False(operation.CanCancel);
        Assert.Throws<InvalidOperationException>(() => operation.AcceptHolding("another"));
        operation.ConfirmFiles();
        Assert.Equal(ManagedRequestPhase.Completed, operation.State.Phase);
        Assert.False(operation.IsActive);
        Assert.Throws<InvalidOperationException>(operation.ConfirmFiles);
    }
    [Fact]
    public void SearchOrCreationProgressCannotDirectlyCompleteFulfillment() {
        var operation = Create();
        Assert.Throws<InvalidOperationException>(operation.ConfirmFiles);
        operation.BeginCreation();
        Assert.Throws<InvalidOperationException>(operation.ConfirmFiles);
    }
    [Fact]
    public void DefiniteRejectionPermitsCancellationButNotRedispatch() {
        var operation = Create(); operation.BeginCreation(); operation.RejectCreation();
        Assert.True(operation.CanCancel);
        Assert.False(operation.IsActive);
        Assert.Throws<InvalidOperationException>(operation.BeginCreation);
        operation.Cancel();
        Assert.Equal(ManagedRequestPhase.Cancelled, operation.State.Phase);
    }
    [Fact]
    public void UnsentIntentCanBeCancelledWithoutInventingARemoteHolding() {
        var operation = Create(); operation.Cancel();
        Assert.Null(operation.State.RemoteId);
        Assert.False(operation.IsActive);
        Assert.Throws<InvalidOperationException>(operation.BeginCreation);
    }
    [Fact]
    public void ReviewDoesNotEraseIdentityOrMakeCreationRepeatable() {
        var operation = Create(); operation.BeginCreation(); operation.AcceptHolding("42"); operation.RequireReview();
        Assert.Equal("42", operation.State.RemoteId);
        Assert.Equal(ManagedRequestPhase.AwaitingFiles, operation.State.Phase);
        Assert.Throws<InvalidOperationException>(operation.BeginCreation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmedRemoteRemovalRetainsIdentityAndOwnerFenceForExplicitRelease(bool completed) {
        var operation = Create();
        operation.AcceptHolding("42");
        if (completed) operation.ConfirmFiles();

        operation.ConfirmRemoteRemoval();

        Assert.Equal(ManagedRequestPhase.RemoteRemoved, operation.State.Phase);
        Assert.Equal("42", operation.State.RemoteId);
        Assert.True(operation.IsActive);
        Assert.False(operation.CanCancel);
        operation.ReleaseOwnership();
        Assert.Equal(ManagedRequestPhase.OwnershipReleased, operation.State.Phase);
    }
}
