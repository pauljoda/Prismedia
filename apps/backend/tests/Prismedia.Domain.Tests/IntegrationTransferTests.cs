using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Domain.Tests;

public sealed class IntegrationTransferTests {
    private const string Instance = "installation";
    private const string Job = "remote-job";
    private const string Revision = "manifest-one";
    private static IntegrationArtifact Artifact => new("file", "item", "book.epub", "application/epub+zip", 100, new string('a', 64), IntegrationArtifactRole.Content);
    private static IntegrationTransfer Submitted() {
        var transfer = IntegrationTransfer.Create(Guid.NewGuid(), Guid.NewGuid(), Instance);
        transfer.BeginSubmission();
        transfer.AcceptSubmission(transfer.State.OperationId, Instance, Job);
        return transfer;
    }
    private static IntegrationTransfer WithManifest() {
        var transfer = Submitted();
        transfer.Observe(Instance, Job, 1, RemoteJobState.Succeeded, Revision);
        transfer.AcceptManifest(new(Job, Revision, true, 1, [Artifact]));
        return transfer;
    }

    [Fact]
    public void DirectSourceCompletesOnlyAfterVerifiedLocalOwnershipWithoutInventingARemoteJob() {
        var transfer = IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(transfer.State.InstanceId);
        Assert.Null(transfer.State.JobId);
        Assert.Throws<InvalidOperationException>(() => transfer.BeginSubmission());
        transfer.AcceptSourceArtifact(Artifact);
        Assert.Equal(IntegrationTransferPhase.Importing, transfer.State.Phase);
        transfer.RecordImported(new(Artifact.Id, Artifact.Sha256, [Guid.NewGuid()]));
        Assert.Equal(IntegrationTransferPhase.Completed, transfer.State.Phase);
        Assert.NotNull(transfer.State.ReceiptId);
        transfer.AcceptSourceArtifact(Artifact);
        Assert.Throws<InvalidOperationException>(() => transfer.AcceptSourceArtifact(Artifact with { SizeBytes = 101 }));
    }

    [Fact]
    public void AmbiguousSubmissionRetainsItsOperationKeyAcrossRehydration() {
        var transfer = IntegrationTransfer.Create(Guid.NewGuid(), Guid.NewGuid(), Instance);
        var operation = transfer.State.OperationId;
        transfer.BeginSubmission();
        var recovered = new IntegrationTransfer(transfer.State);
        Assert.Equal(IntegrationTransferPhase.SubmissionUncertain, recovered.State.Phase);
        recovered.AcceptSubmission(operation, Instance, Job);
        recovered.AcceptSubmission(operation, Instance, Job);
        Assert.Equal(operation, recovered.State.OperationId);
        Assert.Throws<InvalidOperationException>(() => recovered.AcceptSubmission(operation, Instance, "different-job"));
        Assert.Throws<InvalidOperationException>(() => recovered.AcceptSubmission(operation, "replacement-installation", Job));
    }

    [Fact]
    public void RemoteSuccessDoesNotProveBytesOrImportAndStaleSnapshotsAreIgnored() {
        var transfer = Submitted();
        transfer.Observe(Instance, Job, 2, RemoteJobState.Succeeded, Revision);
        transfer.Observe(Instance, Job, 1, RemoteJobState.Running, null);
        Assert.Equal(IntegrationTransferPhase.AwaitingArtifacts, transfer.State.Phase);
        Assert.Null(transfer.State.Imports);
        Assert.Throws<InvalidOperationException>(() => transfer.RecordAcknowledgement(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(RemoteJobState.Partial, IntegrationTransferPhase.NeedsReview)]
    [InlineData(RemoteJobState.Expired, IntegrationTransferPhase.NeedsReview)]
    [InlineData(RemoteJobState.Failed, IntegrationTransferPhase.Failed)]
    [InlineData(RemoteJobState.Cancelled, IntegrationTransferPhase.Cancelled)]
    public void IncompleteOutcomesCannotBecomeSuccessfulImports(RemoteJobState state, IntegrationTransferPhase expected) {
        var transfer = Submitted();
        transfer.Observe(Instance, Job, 1, state, Revision);
        Assert.Equal(expected, transfer.State.Phase);
        Assert.Throws<InvalidOperationException>(() => transfer.AcceptManifest(new(Job, Revision, true, 1, [Artifact])));
    }

    [Fact]
    public void ManifestMutationAndUnverifiedImportsAreRejected() {
        var transfer = WithManifest();
        Assert.Throws<InvalidOperationException>(() => transfer.AcceptManifest(new(Job, Revision, true, 1, [Artifact with { SizeBytes = 200 }])));
        Assert.Throws<InvalidOperationException>(() => transfer.RecordImported(new(Artifact.Id, Artifact.Sha256, [Guid.NewGuid()])));
        Assert.Throws<InvalidOperationException>(() => transfer.RecordVerified(Artifact.Id, 99, Artifact.Sha256));
    }

    [Fact]
    public void ImportReceiptSurvivesRestartAndAcknowledgementRetryWithoutNewTransfers() {
        var transfer = WithManifest();
        transfer.RecordVerified(Artifact.Id, Artifact.SizeBytes, Artifact.Sha256);
        var imported = new IntegrationArtifactImport(Artifact.Id, Artifact.Sha256, [Guid.NewGuid()]);
        transfer.RecordImported(imported);
        var receipt = transfer.State.ReceiptId!.Value;
        var recovered = new IntegrationTransfer(transfer.State);
        recovered.RecordImported(imported);
        Assert.Equal(receipt, recovered.State.ReceiptId);
        Assert.Equal(IntegrationTransferPhase.AwaitingAcknowledgement, recovered.State.Phase);
        recovered.RecordAcknowledgement(receipt);
        recovered.RecordAcknowledgement(receipt);
        Assert.Equal(IntegrationTransferPhase.Completed, recovered.State.Phase);
        Assert.Single(recovered.State.Imports!);
    }
}
