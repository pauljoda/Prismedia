using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionServiceTests {
    private static readonly Guid AcquisitionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid WantedEntityId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid DefaultClientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RecordedClientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CandidateId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string ClientItemId = "download-owned-by-recorded-client";

    [Fact]
    public async Task CreateNormalizesTheNamespaceAndPreservesAnOpaqueColonIdentityValue() {
        var harness = Harness(TransferInfo(RecordedClientId));

        var created = await harness.Service.CreateAndSearchAsync(
            new AcquisitionCreateRequest(
                "Show", null, null, null, null,
                " TMDB ", " Series:Episode:AbC ",
                Kind: EntityKind.VideoSeries),
            CancellationToken.None);

        Assert.Equal(
            new ExternalIdentity("tmdb", "Series:Episode:AbC"),
            harness.Store.CreatedMetadata!.ExternalIdentity);
        Assert.Equal(
            [JobType.AcquisitionSearch, JobType.AcquisitionEnrich],
            harness.Queue.Requests.Select(request => request.Type).ToArray());
        Assert.Equal(AcquisitionStatus.Searching, created.Status);
        Assert.Equal(AcquisitionStatus.Searching, harness.Store.Status);
        Assert.Equal(0, harness.Lifecycle.ExecuteCalls);
    }

    [Fact]
    public async Task EntityBoundCreateHoldsLifecycleThroughPersistenceAndQueuePublication() {
        var harness = Harness(TransferInfo(RecordedClientId));
        harness.Store.BeforeCreate = () => Assert.True(harness.Lifecycle.IsHeld);
        harness.Queue.BeforeEnqueue = () => Assert.True(harness.Lifecycle.IsHeld);

        var created = await harness.Service.CreateAndSearchAsync(
            new AcquisitionCreateRequest(
                "Dune", null, null, null, null, null, null,
                Kind: EntityKind.Book,
                EntityId: WantedEntityId),
            CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Searching, created.Status);
        Assert.Equal(1, harness.Lifecycle.ExecuteCalls);
        Assert.False(harness.Lifecycle.IsHeld);
        Assert.Equal(JobType.AcquisitionSearch, Assert.Single(harness.Queue.Requests).Type);
    }

    [Fact]
    public async Task AlreadyLeasedCreateSeamDoesNotReenterTheEntityLifecycleLease() {
        var harness = Harness(TransferInfo(RecordedClientId));

        var created = await harness.Service.CreateAndSearchWithinEntityLifecycleAsync(
            new AcquisitionCreateRequest(
                "Dune", null, null, null, null, null, null,
                Kind: EntityKind.Book,
                EntityId: WantedEntityId),
            CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Searching, created.Status);
        Assert.Equal(0, harness.Lifecycle.ExecuteCalls);
        Assert.Equal(JobType.AcquisitionSearch, Assert.Single(harness.Queue.Requests).Type);
    }

    [Fact]
    public async Task CreatePublishesSearchingBeforeAnEnqueueFailure() {
        var harness = Harness(TransferInfo(RecordedClientId));
        harness.Queue.EnqueueFailure = new IOException("queue unavailable");

        await Assert.ThrowsAsync<IOException>(() =>
            harness.Service.CreateAndSearchAsync(
                new AcquisitionCreateRequest(
                    "Dune", null, null, null, null, null, null,
                    Kind: EntityKind.Book),
                CancellationToken.None));

        Assert.Equal(AcquisitionStatus.Searching, harness.Store.Status);
        Assert.Empty(harness.Queue.Requests);
    }

    [Fact]
    public async Task CreateRejectsAPartialExternalIdentity() {
        var harness = Harness(TransferInfo(RecordedClientId));

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            harness.Service.CreateAndSearchAsync(
                new AcquisitionCreateRequest(
                    "Show", null, null, null, null,
                    IdentityNamespace: "tmdb", IdentityValue: null),
                CancellationToken.None));

        Assert.Equal(ApiProblemCodes.AcquisitionInvalid, exception.Code);
        Assert.Null(harness.Store.CreatedMetadata);
        Assert.Empty(harness.Queue.Requests);
    }

    [Fact]
    public async Task CreateRejectsWorkWhenTheEntityLifecycleLeaseIsOwnedByCleanup() {
        var harness = Harness(TransferInfo(RecordedClientId));
        harness.Lifecycle.Allow = false;

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            harness.Service.CreateAndSearchAsync(
                new AcquisitionCreateRequest(
                    "Book", null, null, null, null, null, null,
                    Kind: EntityKind.Book, EntityId: WantedEntityId),
                CancellationToken.None));

        Assert.Contains("cleanup", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(harness.Store.CreatedMetadata);
        Assert.Empty(harness.Queue.Requests);
    }

    [Fact]
    public async Task CancelAsyncRemovesTransferFromTheRecordedClient() {
        var harness = Harness(TransferInfo(RecordedClientId));

        await harness.Service.CancelAsync(AcquisitionId, CancellationToken.None);

        Assert.Equal([(RecordedClientId, ClientItemId, true)], harness.Downloads.Removals);
        Assert.Equal(AcquisitionStatus.Cancelled, harness.Store.Status);
        // Cancel stops the download only — the wanted placeholder and its monitor stay untouched.
        Assert.Empty(harness.Monitors.Retargets);
        var entry = Assert.Single(harness.History.Entries);
        Assert.Equal(AcquisitionHistoryEvent.Removed, entry.Event);
        Assert.Equal("Cancelled by user.", entry.Message);
    }

    [Fact]
    public async Task ImportingAcquisitionRejectsCancelDeleteAndResearchWithoutExternalEffects() {
        var harness = Harness(TransferInfo(RecordedClientId, AcquisitionStatus.Importing));

        await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            harness.Service.CancelAsync(AcquisitionId, CancellationToken.None));
        await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            harness.Service.DeleteAsync(AcquisitionId, CancellationToken.None));
        var research = await harness.Service.ReSearchAsync(AcquisitionId, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Importing, research?.Summary.Status);
        Assert.Empty(harness.Downloads.Removals);
        Assert.Empty(harness.Queue.Requests);
        Assert.False(harness.Store.Deleted);
    }

    [Fact]
    public async Task DeleteAsyncRemovesTransferFromTheRecordedClient() {
        var harness = Harness(TransferInfo(RecordedClientId));

        Assert.True(await harness.Service.DeleteAsync(AcquisitionId, CancellationToken.None));

        Assert.Equal([(RecordedClientId, ClientItemId, true)], harness.Downloads.Removals);
        Assert.True(harness.Store.Deleted);
        Assert.Equal([AcquisitionId], harness.JobCleanup.Cancelled);
        var entry = Assert.Single(harness.History.Entries);
        Assert.Equal(AcquisitionHistoryEvent.Removed, entry.Event);
        Assert.Equal("Removed by user.", entry.Message);
    }

    [Fact]
    public async Task StrictUnmonitorDeleteKeepsTheAcquisitionWhenClientRemovalFails() {
        var harness = Harness(TransferInfo(RecordedClientId));
        harness.Downloads.GetFailure = new IOException("client offline");

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            harness.Service.DeleteForUnmonitorAsync(AcquisitionId, CancellationToken.None));

        Assert.Contains("client offline", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(harness.Store.Deleted);
        Assert.Equal(AcquisitionStatus.Stopping, harness.Store.Status);
        Assert.Equal(
            new AcquisitionTeardownClaim(AcquisitionTeardownIntent.Remove, AcquisitionStatus.Downloading),
            harness.Store.TeardownClaim);
        Assert.Contains(
            harness.Store.StatusChanges,
            change => change.Status == AcquisitionStatus.Stopping);
        Assert.Equal([AcquisitionId], harness.JobCleanup.Cancelled);
        Assert.Empty(harness.Downloads.Removals);
        Assert.Empty(harness.History.Entries);
    }

    [Fact]
    public async Task StrictUnmonitorDeleteTreatsAnAlreadyMissingClientItemAsSuccess() {
        var harness = Harness(TransferInfo(RecordedClientId));
        harness.Downloads.ItemExists = false;

        Assert.True(await harness.Service.DeleteForUnmonitorAsync(AcquisitionId, CancellationToken.None));

        Assert.True(harness.Store.Deleted);
        Assert.Empty(harness.Downloads.Removals);
        var entry = Assert.Single(harness.History.Entries);
        Assert.Equal(AcquisitionHistoryEvent.Removed, entry.Event);
    }

    [Fact]
    public async Task CancelAsyncDoesNotRemoveFromDefaultWhenRecordedClientIsMissing() {
        var harness = Harness(TransferInfo(RecordedClientId), includeRecordedClient: false);

        await harness.Service.CancelAsync(AcquisitionId, CancellationToken.None);

        Assert.Empty(harness.Downloads.Removals);
        Assert.Equal(AcquisitionStatus.Cancelled, harness.Store.Status);
        Assert.Empty(harness.Monitors.Retargets);
        var entry = Assert.Single(harness.History.Entries);
        Assert.Equal(AcquisitionHistoryEvent.Removed, entry.Event);
        Assert.Equal("Cancelled by user.", entry.Message);
    }

    [Fact]
    public async Task DeleteAsyncKeepsThePointerWhenRecordedClientIsMissing() {
        var harness = Harness(TransferInfo(RecordedClientId), includeRecordedClient: false);

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            harness.Service.DeleteAsync(AcquisitionId, CancellationToken.None));

        Assert.Contains("recorded download client", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.Downloads.Removals);
        Assert.False(harness.Store.Deleted);
        Assert.NotNull(harness.Store.TransferPointer);
        Assert.Equal(AcquisitionStatus.Stopping, harness.Store.Status);
        Assert.Equal(AcquisitionTeardownIntent.Remove, harness.Store.TeardownClaim?.Intent);
        Assert.Empty(harness.History.Entries);
    }

    [Fact]
    public async Task DeleteAsyncWithPreserveWantedLoopRetargetsTheMonitorAtTheClone() {
        var harness = Harness(TransferInfo(RecordedClientId));
        var cloneId = Guid.NewGuid();
        harness.Store.CloneResult = cloneId;

        Assert.True(await harness.Service.DeleteAsync(AcquisitionId, CancellationToken.None, preserveWantedLoop: true));

        // The download and record are gone, but the monitoring loop survives on the fresh clone.
        Assert.True(harness.Store.Deleted);
        Assert.Equal([(AcquisitionId, cloneId)], harness.Monitors.Retargets);
        Assert.Equal(cloneId, harness.Store.TeardownReplacementId);
    }

    [Fact]
    public async Task PreserveWantedDeleteRetainsItsPointerAndReplacementElectionAcrossRemoteOutageRetry() {
        var harness = Harness(TransferInfo(RecordedClientId));
        var replacementId = Guid.NewGuid();
        harness.Store.CloneResult = replacementId;
        harness.Downloads.RemoveFailure = new IOException("client offline");

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            harness.Service.DeleteAsync(
                AcquisitionId,
                CancellationToken.None,
                preserveWantedLoop: true));

        Assert.Contains("client offline", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(harness.Store.Deleted);
        Assert.NotNull(harness.Store.TransferPointer);
        Assert.Equal(AcquisitionStatus.Stopping, harness.Store.Status);
        Assert.Equal(
            AcquisitionTeardownIntent.Reacquire,
            harness.Store.TeardownClaim?.Intent);
        Assert.Null(harness.Store.TeardownReplacementId);
        Assert.Empty(harness.Monitors.Retargets);

        harness.Downloads.RemoveFailure = null;
        Assert.True(await harness.Service.DeleteAsync(
            AcquisitionId,
            CancellationToken.None,
            preserveWantedLoop: true));

        Assert.True(harness.Store.Deleted);
        Assert.Equal(replacementId, harness.Store.TeardownReplacementId);
        Assert.Equal([(AcquisitionId, replacementId)], harness.Monitors.Retargets);
        Assert.Equal(0, harness.Store.CloneCalls);
    }

    [Fact]
    public async Task PreserveWantedDeleteRemovesCloneWhenStoppingClaimWinsRetargetRace() {
        var harness = Harness(TransferInfo(RecordedClientId));
        var cloneId = Guid.NewGuid();
        harness.Store.CloneResult = cloneId;
        harness.Monitors.RetargetSucceeds = false;

        Assert.True(await harness.Service.DeleteAsync(
            AcquisitionId, CancellationToken.None, preserveWantedLoop: true));

        Assert.Equal([cloneId, AcquisitionId], harness.Store.DeletedIds);
        Assert.Equal([(AcquisitionId, cloneId)], harness.Monitors.Retargets);
        Assert.Equal([(RecordedClientId, ClientItemId, true)], harness.Downloads.Removals);
    }

    [Fact]
    public async Task DeleteAsyncWithoutPreserveNeverClonesOrRetargets() {
        var harness = Harness(TransferInfo(RecordedClientId));
        harness.Store.CloneResult = Guid.NewGuid();

        Assert.True(await harness.Service.DeleteAsync(AcquisitionId, CancellationToken.None));

        Assert.True(harness.Store.Deleted);
        Assert.Empty(harness.Monitors.Retargets);
    }

    [Fact]
    public async Task QueueNeverContactsTheClientWhenTeardownWinsBeforeTheTransferAddLease() {
        var harness = Harness(new AcquisitionTransferInfo(
            AcquisitionStatus.AwaitingSelection, null, null, null));
        PrepareQueueCandidate(harness.Store);
        var transferAdds = new RecordingTransferAddCoordinator {
            OnAcquire = () => harness.Store.ForceStatus(AcquisitionStatus.Stopping),
            Allow = false
        };
        var queue = QueueService(harness, transferAdds);

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            queue.QueueAsync(AcquisitionId, CandidateId, CancellationToken.None));

        Assert.Contains("began cleanup", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, harness.Downloads.AddCount);
        Assert.Null(harness.Store.TransferPointer?.ClientItemId);
        Assert.Null(harness.Store.TransferPointer?.State);
        Assert.Equal(AcquisitionStatus.Stopping, harness.Store.Status);
        Assert.Equal(1, transferAdds.AcquireCount);
    }

    [Fact]
    public async Task AutomaticQueueCannotReviveACancelledSearchResult() {
        var harness = Harness(new AcquisitionTransferInfo(
            AcquisitionStatus.Cancelled, null, null, null));
        PrepareQueueCandidate(harness.Store);

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            QueueService(harness, new RecordingTransferAddCoordinator())
                .QueueAsync(
                    AcquisitionId,
                    CandidateId,
                    CancellationToken.None,
                    requiredStatus: AcquisitionStatus.AwaitingSelection));

        Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AcquisitionStatus.Cancelled, harness.Store.Status);
        Assert.Equal(0, harness.Downloads.AddCount);
        Assert.Empty(harness.Store.StatusChanges);
    }

    [Fact]
    public async Task QueueRetainsACrashRecoveryPointerWhenTeardownWinsBeforeItsRecoveryLease() {
        var harness = Harness(new AcquisitionTransferInfo(
            AcquisitionStatus.Queued,
            null,
            "abc123",
            RecordedClientId,
            "prismedia",
            TransferOwnershipState.Adding.ToCode()));
        PrepareQueueCandidate(harness.Store);
        var transferAdds = new RecordingTransferAddCoordinator {
            OnAcquire = () => harness.Store.ForceStatus(AcquisitionStatus.Stopping),
            Allow = false
        };

        await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            QueueService(harness, transferAdds)
                .QueueAsync(AcquisitionId, CandidateId, CancellationToken.None));

        Assert.Equal(0, harness.Downloads.AddCount);
        Assert.Equal("abc123", harness.Store.TransferPointer?.ClientItemId);
        Assert.Equal(TransferOwnershipState.Adding.ToCode(), harness.Store.TransferPointer?.State);
        Assert.Equal(AcquisitionStatus.Stopping, harness.Store.Status);
    }

    [Fact]
    public async Task QueueKeepsDurableCorrelationWhenCompletionLosesCallerCancellation() {
        var harness = Harness(new AcquisitionTransferInfo(
            AcquisitionStatus.AwaitingSelection, null, null, null));
        PrepareQueueCandidate(harness.Store);
        harness.Store.CreateTransferResult = false;
        using var callerCancellation = new CancellationTokenSource();
        harness.Downloads.OnAdd = callerCancellation.Cancel;
        var transferAdds = new RecordingTransferAddCoordinator();
        var queue = QueueService(harness, transferAdds);

        await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            queue.QueueAsync(AcquisitionId, CandidateId, callerCancellation.Token));

        Assert.Equal(1, harness.Downloads.AddCount);
        Assert.False(harness.Store.CreateTransferCancellationToken.CanBeCanceled);
        Assert.Empty(harness.Downloads.RemovalCancellationTokens);
        Assert.Equal("abc123", harness.Store.TransferPointer?.ClientItemId);
        Assert.Equal(TransferOwnershipState.Adding.ToCode(), harness.Store.TransferPointer?.State);
        Assert.False(transferAdds.Lease.Committed);
    }

    [Fact]
    public async Task QueueCompensatesANewRemoteAddWhenCancellationWinsFinalization() {
        var harness = Harness(new AcquisitionTransferInfo(
            AcquisitionStatus.AwaitingSelection, null, null, null));
        PrepareQueueCandidate(harness.Store);
        harness.Store.CreateTransferResult = false;
        harness.Store.BeforeCreateTransfer = () => harness.Store.ForceStatus(AcquisitionStatus.Cancelled);

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            QueueService(harness, new RecordingTransferAddCoordinator())
                .QueueAsync(AcquisitionId, CandidateId, CancellationToken.None));

        Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AcquisitionStatus.Cancelled, harness.Store.Status);
        Assert.Equal([(RecordedClientId, "new-client-item", true)], harness.Downloads.Removals);
        Assert.Null(harness.Store.TransferPointer?.ClientItemId);
        Assert.Null(harness.Store.TransferPointer?.State);
    }

    [Fact]
    public async Task QueueRetainsDurableOwnershipWhenCancellationCompensationCannotReachTheClient() {
        var harness = Harness(new AcquisitionTransferInfo(
            AcquisitionStatus.AwaitingSelection, null, null, null));
        PrepareQueueCandidate(harness.Store);
        harness.Store.CreateTransferResult = false;
        harness.Store.BeforeCreateTransfer = () => harness.Store.ForceStatus(AcquisitionStatus.Cancelled);
        harness.Downloads.RemoveFailure = new IOException("client offline");

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            QueueService(harness, new RecordingTransferAddCoordinator())
                .QueueAsync(AcquisitionId, CandidateId, CancellationToken.None));

        Assert.Equal(ApiProblemCodes.DownloadClientUnreachable, exception.Code);
        Assert.Contains("durable ownership marker", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AcquisitionStatus.Cancelled, harness.Store.Status);
        Assert.Equal("abc123", harness.Store.TransferPointer?.ClientItemId);
        Assert.Equal(TransferOwnershipState.Adding.ToCode(), harness.Store.TransferPointer?.State);
    }

    [Fact]
    public async Task ManualTorrentQueueCompensatesANewRemoteAddWhenCancellationWinsFinalization() {
        var harness = Harness(new AcquisitionTransferInfo(
            AcquisitionStatus.AwaitingSelection, null, null, null));
        harness.Store.CreateTransferResult = false;
        harness.Store.BeforeCreateTransfer = () => harness.Store.ForceStatus(AcquisitionStatus.Cancelled);

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            QueueService(harness, new RecordingTransferAddCoordinator())
                .QueueManualTorrentAsync(
                    AcquisitionId,
                    "Dune.torrent",
                    [1, 2, 3],
                    CancellationToken.None));

        Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AcquisitionStatus.Cancelled, harness.Store.Status);
        Assert.Equal([(RecordedClientId, "new-client-item", true)], harness.Downloads.Removals);
        Assert.Null(harness.Store.TransferPointer?.ClientItemId);
        Assert.Null(harness.Store.TransferPointer?.State);
    }

    [Fact]
    public async Task QueueRetryRecoversTheRemoteItemWithoutASecondPlaceholderOrAdd() {
        var harness = Harness(new AcquisitionTransferInfo(
            AcquisitionStatus.AwaitingSelection, null, null, null));
        PrepareQueueCandidate(harness.Store);
        harness.Store.CreateTransferResult = false;
        var firstLease = new RecordingTransferAddCoordinator();
        var queue = QueueService(harness, firstLease);
        await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            queue.QueueAsync(AcquisitionId, CandidateId, CancellationToken.None));

        harness.Store.CreateTransferResult = true;
        var retryLease = new RecordingTransferAddCoordinator();
        await QueueService(harness, retryLease)
            .QueueAsync(AcquisitionId, CandidateId, CancellationToken.None);

        Assert.Equal(1, harness.Store.BeginTransferAddCalls);
        Assert.Equal(1, harness.Downloads.AddCount);
        Assert.Null(harness.Store.TransferPointer?.State);
        Assert.Equal("abc123", harness.Store.TransferPointer?.ClientItemId);
        Assert.True(retryLease.Lease.Committed);
    }

    [Fact]
    public async Task TeardownRecoversOneTitleCorrelatedAddButFailsClosedOnAmbiguity() {
        var unique = Harness(new AcquisitionTransferInfo(
            AcquisitionStatus.Queued,
            null,
            "Dune Release",
            RecordedClientId,
            "prismedia",
            TransferOwnershipState.Adding.ToCode()));
        unique.Downloads.Items.Add(new DownloadItemStatus(
            "nzo-1", "Dune.Release", 0.2, "downloading", false, null, null));

        Assert.True(await unique.Service.DeleteForUnmonitorAsync(AcquisitionId, CancellationToken.None));
        Assert.Contains(unique.Downloads.Removals, removal => removal.ClientItemId == "nzo-1");

        var ambiguous = Harness(new AcquisitionTransferInfo(
            AcquisitionStatus.Queued,
            null,
            "Dune Release",
            RecordedClientId,
            "prismedia",
            TransferOwnershipState.Adding.ToCode()));
        ambiguous.Downloads.Items.AddRange([
            new DownloadItemStatus("nzo-1", "Dune.Release", 0.2, "downloading", false, null, null),
            new DownloadItemStatus("nzo-2", "Dune Release", 0.3, "downloading", false, null, null)
        ]);

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            ambiguous.Service.DeleteForUnmonitorAsync(AcquisitionId, CancellationToken.None));

        Assert.Contains("multiple client items", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(ambiguous.Store.Deleted);
        Assert.NotNull(ambiguous.Store.TransferPointer);
        Assert.Empty(ambiguous.Downloads.Removals);
    }

    [Fact]
    public async Task TeardownFailsClosedWhenAnUnfinishedAddCannotBeResolved() {
        var harness = Harness(new AcquisitionTransferInfo(
            AcquisitionStatus.Queued,
            null,
            "Dune Release",
            RecordedClientId,
            "prismedia",
            TransferOwnershipState.Adding.ToCode()));
        harness.Downloads.ItemExists = false;

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            harness.Service.DeleteForUnmonitorAsync(AcquisitionId, CancellationToken.None));

        Assert.Contains("could not be resolved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(harness.Store.Deleted);
        Assert.NotNull(harness.Store.TransferPointer);
        Assert.Equal(AcquisitionStatus.Stopping, harness.Store.Status);
        Assert.Empty(harness.Downloads.Removals);
    }

    [Fact]
    public async Task RequeuePriorRemovalFailurePreservesTheOldPointerAndAbortsBeforeAdd() {
        var harness = Harness(new AcquisitionTransferInfo(
            AcquisitionStatus.Failed, null, ClientItemId, RecordedClientId));
        PrepareQueueCandidate(harness.Store);
        harness.Downloads.RemoveFailure = new IOException("client offline");
        var transferAdds = new RecordingTransferAddCoordinator();
        var queue = QueueService(harness, transferAdds);

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            queue.QueueAsync(AcquisitionId, CandidateId, CancellationToken.None));

        Assert.Contains("replacement was not queued", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AcquisitionStatus.Failed, harness.Store.Status);
        Assert.Equal(ClientItemId, harness.Store.TransferPointer?.ClientItemId);
        Assert.Equal(0, harness.Downloads.AddCount);
        Assert.Equal(0, transferAdds.AcquireCount);
    }

    [Fact]
    public async Task ReacquireEligibilityRejectsAnActiveAcquisitionWithoutSideEffects() {
        var harness = Harness(TransferInfo(RecordedClientId, AcquisitionStatus.Downloading));

        var result = await harness.Service.GetReacquireEligibilityAsync(AcquisitionId, CancellationToken.None);

        Assert.False(result.CanReacquire);
        Assert.Contains("downloading", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.Downloads.Removals);
        Assert.Empty(harness.Store.StatusChanges);
        Assert.Empty(harness.Queue.Requests);
        Assert.Empty(harness.Monitors.Retargets);
        Assert.False(harness.Store.Deleted);
    }

    [Fact]
    public async Task RemovalEligibilityRejectsImportingWithoutAnyCleanupSideEffects() {
        var harness = Harness(TransferInfo(RecordedClientId, AcquisitionStatus.Importing));

        var result = await harness.Service.GetRemovalEligibilityAsync(AcquisitionId, CancellationToken.None);

        Assert.False(result.CanRemove);
        Assert.Contains("importing", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.JobCleanup.Cancelled);
        Assert.Empty(harness.Downloads.Removals);
        Assert.Empty(harness.Store.StatusChanges);
        Assert.False(harness.Store.Deleted);
    }

    [Fact]
    public async Task RemovalEligibilityRejectsPartiallyPlacedBookCheckpoint() {
        var harness = Harness(TransferInfo(RecordedClientId, AcquisitionStatus.Failed));
        harness.Store.ImportContext = PartialBookImportContext();

        var result = await harness.Service.GetRemovalEligibilityAsync(AcquisitionId, CancellationToken.None);

        Assert.False(result.CanRemove);
        Assert.Contains("partially applied import", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.Store.StatusChanges);
        Assert.False(harness.Store.Deleted);
    }

    [Fact]
    public async Task ReSearchRejectsPartiallyPlacedBookCheckpointWithoutQueueing() {
        var harness = Harness(TransferInfo(RecordedClientId, AcquisitionStatus.Failed));
        harness.Store.ImportContext = PartialBookImportContext();

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            harness.Service.ReSearchAsync(AcquisitionId, CancellationToken.None));

        Assert.Contains("partially applied import", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.Store.StatusChanges);
        Assert.Empty(harness.Queue.Requests);
    }

    [Fact]
    public async Task ReSearchClearsUntouchedBookCheckpointThenQueuesNormally() {
        var harness = Harness(TransferInfo(RecordedClientId, AcquisitionStatus.Failed));
        harness.Store.ImportContext = PartialBookImportContext(mutationCompleted: false);

        var refreshed = await harness.Service.ReSearchAsync(AcquisitionId, CancellationToken.None);

        Assert.Equal(1, harness.Store.ClearedPlacementCheckpoints);
        Assert.Equal(AcquisitionStatus.Searching, refreshed?.Summary.Status);
        Assert.Contains(
            (AcquisitionId, AcquisitionStatus.Searching, (string?)null),
            harness.Store.StatusChanges);
        Assert.Single(harness.Queue.Requests);
    }

    [Fact]
    public async Task ExplicitResearchRevivesCancelledBeforeEnqueueing() {
        var harness = Harness(TransferInfo(RecordedClientId, AcquisitionStatus.Cancelled));

        var refreshed = await harness.Service.ReSearchAsync(AcquisitionId, CancellationToken.None);

        Assert.Equal(AcquisitionStatus.Searching, harness.Store.Status);
        Assert.Equal(AcquisitionStatus.Searching, refreshed?.Summary.Status);
        Assert.Contains(
            (AcquisitionId, AcquisitionStatus.Searching, (string?)null),
            harness.Store.StatusChanges);
        Assert.Equal(JobType.AcquisitionSearch, Assert.Single(harness.Queue.Requests).Type);
    }

    [Theory]
    [InlineData(AcquisitionStatus.Pending)]
    [InlineData(AcquisitionStatus.AwaitingSelection)]
    [InlineData(AcquisitionStatus.Failed)]
    [InlineData(AcquisitionStatus.ManualImportRequired)]
    [InlineData(AcquisitionStatus.Cancelled)]
    public async Task RedeliveredSearchWithoutSearchingIntentPreservesCurrentState(
        AcquisitionStatus status) {
        var harness = Harness(TransferInfo(RecordedClientId, status));
        harness.Store.SearchInput = new AcquisitionSearchInput(
            AcquisitionId,
            "Dune",
            "Frank Herbert",
            EntityKind.Book,
            WantedEntityId);
        var now = DateTimeOffset.UtcNow;
        var job = new JobRunSnapshot(
            Guid.NewGuid(),
            JobType.AcquisitionSearch,
            JobRunStatus.Running,
            0,
            null,
            AcquisitionJobPayload.Serialize(AcquisitionId),
            null,
            AcquisitionId.ToString(),
            "Dune",
            now,
            now,
            null);
        var handler = new AcquisitionSearchJobHandler(
            harness.Store,
            runner: null!,
            profiles: null!,
            queue: null!,
            NullLogger<AcquisitionSearchJobHandler>.Instance);

        await handler.HandleAsync(
            new JobContext(job, harness.Queue),
            CancellationToken.None);

        Assert.Equal(status, harness.Store.Status);
        Assert.Empty(harness.Store.StatusChanges);
        Assert.Empty(harness.Queue.Requests);
    }

    [Fact]
    public async Task ReacquireEligibilityAllowsAnImportedAcquisitionWithoutSideEffects() {
        var harness = Harness(TransferInfo(RecordedClientId, AcquisitionStatus.Imported));

        var result = await harness.Service.GetReacquireEligibilityAsync(AcquisitionId, CancellationToken.None);

        Assert.True(result.CanReacquire);
        Assert.Null(result.Message);
        Assert.Empty(harness.Downloads.Removals);
        Assert.Empty(harness.Store.StatusChanges);
        Assert.Empty(harness.Queue.Requests);
        Assert.Empty(harness.Monitors.Retargets);
        Assert.False(harness.Store.Deleted);
    }

    [Fact]
    public async Task ReacquireAsyncReplacesImportedStateAndImmediatelySearchesTheClone() {
        var harness = Harness(TransferInfo(RecordedClientId, AcquisitionStatus.Imported));
        var replacementId = Guid.NewGuid();
        harness.Store.CloneResult = replacementId;

        var result = await harness.Service.ReacquireAsync(AcquisitionId, CancellationToken.None);

        Assert.Equal(replacementId, result);
        Assert.Equal([(RecordedClientId, ClientItemId, true)], harness.Downloads.Removals);
        Assert.Equal([(AcquisitionId, replacementId)], harness.Monitors.Retargets);
        Assert.Contains(
            (replacementId, AcquisitionStatus.Searching, (string?)null),
            harness.Store.StatusChanges);
        var search = Assert.Single(harness.Queue.Requests);
        Assert.Equal(JobType.AcquisitionSearch, search.Type);
        Assert.Equal(replacementId.ToString(), search.TargetEntityId);
        Assert.Equal(replacementId, AcquisitionJobPayload.Parse(search.PayloadJson!).AcquisitionId);
        Assert.True(harness.Store.Deleted);
        var history = Assert.Single(harness.History.Entries);
        Assert.Equal(AcquisitionHistoryEvent.Removed, history.Event);
        Assert.Equal("Files deleted; searching again.", history.Message);
    }

    [Fact]
    public async Task ReacquireRemovesPendingCloneAndQueuesNothingWhenStoppingClaimWins() {
        var harness = Harness(TransferInfo(RecordedClientId, AcquisitionStatus.Imported));
        var replacementId = Guid.NewGuid();
        harness.Store.CloneResult = replacementId;
        harness.Monitors.RetargetSucceeds = false;

        var result = await harness.Service.ReacquireAsync(AcquisitionId, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal([replacementId, AcquisitionId], harness.Store.DeletedIds);
        Assert.Empty(harness.Queue.Requests);
        Assert.DoesNotContain(
            harness.Store.StatusChanges,
            change => change.Id == replacementId && change.Status == AcquisitionStatus.Searching);
    }

    [Fact]
    public async Task ReacquireAsyncCloneFailureRemovesTheUnusableImportedStateAndMonitor() {
        var harness = Harness(TransferInfo(RecordedClientId, AcquisitionStatus.Imported));
        harness.Store.CloneResult = null;

        var result = await harness.Service.ReacquireAsync(AcquisitionId, CancellationToken.None);

        Assert.Null(result);
        Assert.True(harness.Store.Deleted);
        Assert.Equal([harness.Monitors.MonitorId], harness.Monitors.Deleted);
        Assert.Empty(harness.Monitors.Retargets);
        Assert.Empty(harness.Queue.Requests);
        var history = Assert.Single(harness.History.Entries);
        Assert.Equal(AcquisitionHistoryEvent.Removed, history.Event);
        Assert.Equal("Files deleted; retry could not be initialized.", history.Message);
    }

    [Fact]
    public async Task ReacquireClientOutagePreservesImportedStateAndDoesNotStartAReplacement() {
        var harness = Harness(TransferInfo(RecordedClientId, AcquisitionStatus.Imported));
        harness.Downloads.GetFailure = new IOException("client offline");

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            harness.Service.ReacquireAsync(AcquisitionId, CancellationToken.None));

        Assert.Contains("client offline", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AcquisitionStatus.Stopping, harness.Store.Status);
        Assert.Equal(
            new AcquisitionTeardownClaim(AcquisitionTeardownIntent.Reacquire, AcquisitionStatus.Imported),
            harness.Store.TeardownClaim);
        Assert.Contains(
            harness.Store.StatusChanges,
            change => change.Status == AcquisitionStatus.Stopping);
        Assert.Equal([AcquisitionId], harness.JobCleanup.Cancelled);
        Assert.False(harness.Store.Deleted);
        Assert.Empty(harness.Downloads.Removals);
        Assert.Empty(harness.Monitors.Retargets);
        Assert.Empty(harness.Monitors.Deleted);
        Assert.Empty(harness.Queue.Requests);
        Assert.Empty(harness.History.Entries);
    }

    [Fact]
    public async Task FailedDurableImportCanEnqueueAnExplicitResume() {
        var harness = Harness(TransferInfo(RecordedClientId, AcquisitionStatus.Failed));
        harness.Store.HasResumableImport = true;

        var detail = await harness.Service.RetryImportAsync(
            AcquisitionId,
            allowFormatChange: false,
            CancellationToken.None);

        Assert.True(detail?.Summary.HasResumableImport);
        var retry = Assert.Single(harness.Queue.Requests);
        Assert.Equal(JobType.AcquisitionImport, retry.Type);
        var payload = AcquisitionJobPayload.Parse(retry.PayloadJson!);
        Assert.Equal(AcquisitionId, payload.AcquisitionId);
        Assert.True(payload.ManualRetry);
    }

    [Fact]
    public async Task DeleteAsyncUsesDefaultClientOnlyForLegacyTransfersWithoutRecordedClient() {
        var harness = Harness(TransferInfo(downloadClientConfigId: null));

        Assert.True(await harness.Service.DeleteAsync(AcquisitionId, CancellationToken.None));

        Assert.Equal([(DefaultClientId, ClientItemId, true)], harness.Downloads.Removals);
        Assert.True(harness.Store.Deleted);
    }

    private static AcquisitionTransferInfo TransferInfo(
        Guid? downloadClientConfigId,
        AcquisitionStatus status = AcquisitionStatus.Downloading) =>
        new(status, FinalSourcePath: null, ClientItemId, downloadClientConfigId);

    private static AcquisitionImportContext PartialBookImportContext(bool mutationCompleted = true) {
        var boundary = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            $"prismedia-partial-book-{Guid.NewGuid():N}"));
        var payloadRoot = Path.Combine(boundary, "payload");
        var libraryRoot = Path.Combine(boundary, "library");
        var source = Path.Combine(payloadRoot, "Dune.epub");
        var target = Path.Combine(libraryRoot, "Frank Herbert", "Dune.epub");
        if (!mutationCompleted) {
            Directory.CreateDirectory(payloadRoot);
            File.WriteAllText(source, "download-bytes");
        }
        var checkpoint = new ImportPlacementCheckpoint(
            EntityKind.Book,
            Guid.NewGuid(),
            libraryRoot,
            payloadRoot,
            ImportMode.Move,
            Path.GetDirectoryName(target)!,
            Path.GetDirectoryName(target)!,
            "Imported into the library.",
            [new ImportPlacementCheckpointUnit(
                "Dune.epub",
                source,
                target,
                IsMedia: true,
                FinalPath: mutationCompleted ? target : null)],
            AttemptId: Guid.NewGuid(),
            ClaimJobId: Guid.NewGuid());
        return new AcquisitionImportContext(
            AcquisitionId,
            "Dune",
            "Frank Herbert",
            Series: null,
            Year: 1965,
            PosterUrl: null,
            ExternalIdentity: null,
            ProfileId: null,
            ContentPath: payloadRoot,
            ClientItemId: ClientItemId,
            DownloadClientConfigId: RecordedClientId,
            Kind: EntityKind.Book,
            EntityId: WantedEntityId,
            ImportPlacementCheckpoint: checkpoint);
    }

    private static TestHarness Harness(AcquisitionTransferInfo transfer, bool includeRecordedClient = true) {
        var store = new FakeAcquisitionStore(transfer);
        var downloads = new RecordingDownloadClientFactory();
        var configs = new FakeDownloadClientConfigStore(includeRecordedClient);
        var history = new FakeAcquisitionHistoryStore();
        var monitors = new RecordingMonitorStore();
        var queue = new RecordingJobQueue();
        var jobCleanup = new RecordingAcquisitionJobCleanup();
        var lifecycle = new RecordingEntityLifecycleLease();
        var service = new AcquisitionService(
            store,
            new ThrowingBlocklistStore(),
            queue,
            configs,
            downloads,
            new EmptyImportedFilesReader(),
            history,
            NullLogger<AcquisitionService>.Instance,
            monitors,
            jobCleanup,
            lifecycle);

        return new TestHarness(service, store, downloads, history, monitors, queue, jobCleanup, lifecycle);
    }

    private static void PrepareQueueCandidate(FakeAcquisitionStore store) {
        store.QueueCandidate = new AcquisitionQueueCandidate(
            CandidateId,
            "Dune.2021.1080p",
            "Indexer",
            "https://indexer.test/download",
            null,
            "ABC123",
            null,
            DownloadProtocol.Torrent);
        store.SearchInput = new AcquisitionSearchInput(
            AcquisitionId,
            "Dune",
            "Frank Herbert",
            EntityKind.Book,
            WantedEntityId);
    }

    private static AcquisitionQueueService QueueService(
        TestHarness harness,
        IAcquisitionTransferAddCoordinator transferAdds) =>
        new(
            harness.Store,
            new ThrowingBlocklistStore(),
            new FakeDownloadClientConfigStore(includeRecordedClient: true),
            harness.Downloads,
            new NullAcquisitionProfileStore(),
            new NullIndexerConfigStore(),
            new NullReleaseLinkResolver(),
            transferAdds,
            harness.History,
            NullLogger<AcquisitionQueueService>.Instance);

    private sealed record TestHarness(
        AcquisitionService Service,
        FakeAcquisitionStore Store,
        RecordingDownloadClientFactory Downloads,
        FakeAcquisitionHistoryStore History,
        RecordingMonitorStore Monitors,
        RecordingJobQueue Queue,
        RecordingAcquisitionJobCleanup JobCleanup,
        RecordingEntityLifecycleLease Lifecycle);

    private sealed class RecordingEntityLifecycleLease : IEntityLifecycleMutationLease {
        public bool Allow { get; set; } = true;
        public int ExecuteCalls { get; private set; }
        public bool IsHeld { get; private set; }

        public async Task<bool> ExecuteAsync(
            Guid entityId,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken) {
            ExecuteCalls++;
            if (!Allow) {
                return false;
            }

            IsHeld = true;
            try {
                await mutation(cancellationToken);
            } finally {
                IsHeld = false;
            }
            return true;
        }
    }

    private sealed class RecordingTransferAddCoordinator : IAcquisitionTransferAddCoordinator {
        public bool Allow { get; set; } = true;
        public Action? OnAcquire { get; set; }
        public int AcquireCount { get; private set; }
        public RecordingTransferAddLease Lease { get; } = new();

        public Task<IAcquisitionTransferAddLease?> AcquireAsync(
            Guid acquisitionId,
            CancellationToken cancellationToken) {
            AcquireCount++;
            OnAcquire?.Invoke();
            return Task.FromResult<IAcquisitionTransferAddLease?>(Allow ? Lease : null);
        }
    }

    private sealed class RecordingTransferAddLease : IAcquisitionTransferAddLease {
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken) {
            Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingAcquisitionJobCleanup : IAcquisitionJobCleanup {
        public List<Guid> Cancelled { get; } = [];

        public Task<int> CancelAsync(Guid acquisitionId, CancellationToken cancellationToken) {
            Cancelled.Add(acquisitionId);
            return Task.FromResult(1);
        }
    }

    private sealed class FakeAcquisitionStore(AcquisitionTransferInfo transfer) : IAcquisitionStore {
        private readonly AcquisitionSummary _summary = new(
            AcquisitionId,
            AcquisitionStatus.Downloading,
            null,
            "Dune",
            "Frank Herbert",
            null,
            1965,
            null,
            0.25,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            Kind: EntityKind.Book,
            EntityId: WantedEntityId);

        public AcquisitionStatus? Status { get; private set; } = transfer.Status;
        public AcquisitionTeardownClaim? TeardownClaim { get; private set; }
        public AcquisitionStatus? ReplacementStatus { get; private set; }
        public List<(Guid Id, AcquisitionStatus Status, string? Message)> StatusChanges { get; } = [];
        public bool Deleted { get; private set; }
        public List<Guid> DeletedIds { get; } = [];
        public bool HasResumableImport { get; set; }
        public AcquisitionMetadata? CreatedMetadata { get; private set; }
        public Action? BeforeCreate { get; set; }
        public AcquisitionTransferInfo? TransferPointer { get; private set; } = transfer;
        public AcquisitionQueueCandidate? QueueCandidate { get; set; }
        public AcquisitionSearchInput? SearchInput { get; set; }
        public bool CreateTransferResult { get; set; } = true;
        public bool PersistRejectedTransfer { get; set; }
        public Action? BeforeCreateTransfer { get; set; }
        public CancellationToken CreateTransferCancellationToken { get; private set; }
        public SelectedRelease? SelectedRelease { get; private set; }
        public int CloneCalls { get; private set; }
        public Guid? TeardownReplacementId { get; private set; }
        public int BeginTransferAddCalls { get; private set; }
        public AcquisitionImportContext? ImportContext { get; set; }
        public int ClearedPlacementCheckpoints { get; private set; }

        public Task<AcquisitionDetail?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<AcquisitionDetail?>(id == AcquisitionId
                ? new AcquisitionDetail(_summary with {
                    Status = Status ?? _summary.Status,
                    HasResumableImport = HasResumableImport,
                }, [])
                : null);

        public Task SetStatusAsync(Guid id, AcquisitionStatus status, string? message, CancellationToken cancellationToken) {
            StatusChanges.Add((id, status, message));
            if (id == AcquisitionId) {
                Status = status;
            } else if (id == CloneResult) {
                ReplacementStatus = status;
            }
            return Task.CompletedTask;
        }

        public void ForceStatus(AcquisitionStatus status) => Status = status;

        public async Task<bool> TryTransitionStatusAsync(
            Guid id,
            IReadOnlyCollection<AcquisitionStatus> expectedStatuses,
            AcquisitionStatus status,
            string? message,
            CancellationToken cancellationToken) {
            var current = id == AcquisitionId ? Status : id == CloneResult ? ReplacementStatus : null;
            if (current is null || !expectedStatuses.Contains(current.Value)) {
                return false;
            }

            await SetStatusAsync(id, status, message, cancellationToken);
            return true;
        }

        public async Task<bool> TryClaimFailedRecoveryAsync(
            Guid id,
            IReadOnlyCollection<AcquisitionStatus> expectedStatuses,
            SelectedRelease? expectedSelectedRelease,
            string message,
            CancellationToken cancellationToken) {
            if (id != AcquisitionId
                || Status is not { } current
                || !expectedStatuses.Contains(current)
                || SelectedRelease != expectedSelectedRelease) {
                return false;
            }

            await SetStatusAsync(id, AcquisitionStatus.Failed, message, cancellationToken);
            return true;
        }

        public Task<AcquisitionTeardownClaim?> GetTeardownClaimAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == AcquisitionId ? TeardownClaim : null);

        public async Task<bool> TryClaimTeardownAsync(
            Guid id,
            AcquisitionStatus expectedStatus,
            AcquisitionTeardownIntent intent,
            string message,
            CancellationToken cancellationToken) {
            if (id != AcquisitionId || Status != expectedStatus || TeardownClaim is not null) {
                return false;
            }

            TeardownClaim = new AcquisitionTeardownClaim(intent, expectedStatus);
            await SetStatusAsync(id, AcquisitionStatus.Stopping, message, cancellationToken);
            return true;
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) {
            DeletedIds.Add(id);
            if (id == AcquisitionId) {
                Deleted = true;
            }
            return Task.FromResult(true);
        }

        /// <summary>What CloneForRetryAsync returns — set by tests exercising the preserve-wanted-loop path.</summary>
        public Guid? CloneResult { get; set; }

        public Task<Guid?> CloneForRetryAsync(Guid id, CancellationToken cancellationToken) {
            Assert.Equal(AcquisitionId, id);
            CloneCalls++;
            return Task.FromResult(CloneResult);
        }

        public Task<Guid?> GetOrCreateTeardownReplacementAsync(Guid id, CancellationToken cancellationToken) {
            Assert.Equal(AcquisitionId, id);
            if (!TeardownReplacementId.HasValue && CloneResult.HasValue) {
                TeardownReplacementId = CloneResult;
                ReplacementStatus ??= AcquisitionStatus.Pending;
            }
            return Task.FromResult(TeardownReplacementId);
        }

        public Task<AcquisitionTransferInfo?> GetTransferInfoAsync(Guid acquisitionId, CancellationToken cancellationToken) =>
            Task.FromResult(acquisitionId == AcquisitionId ? TransferPointer : null);

        public Task<AcquisitionSummary> CreateAsync(AcquisitionMetadata metadata, CancellationToken cancellationToken) {
            BeforeCreate?.Invoke();
            CreatedMetadata = metadata;
            Status = AcquisitionStatus.Pending;
            return Task.FromResult(_summary with {
                Status = AcquisitionStatus.Pending,
                Title = metadata.Title,
                Kind = metadata.Kind,
                EntityId = metadata.EntityId
            });
        }
        public Task<IReadOnlyList<Guid>> ListStaleSearchingAsync(TimeSpan olderThan, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AcquisitionSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AcquisitionSearchInput?> GetSearchInputAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == AcquisitionId ? SearchInput : null);
        public Task<AcquisitionStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == AcquisitionId ? Status : id == CloneResult ? ReplacementStatus : null);
        public Task<UpgradeOwnedQuality?> GetUpgradeOwnedQualityAsync(Guid acquisitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UpgradeReplaceTarget?> GetUpgradeReplaceTargetAsync(Guid childId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateOwnedQualityAsync(Guid acquisitionId, BookQualityRank ownedQuality, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateOwnedMediaQualityAsync(Guid acquisitionId, string ownedMediaQuality, int ownedMediaRevision, int ownedFormatScore, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnrichMetadataAsync(Guid acquisitionId, string? description, string? posterUrl, int? year, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkImportedWithQualityAsync(Guid id, BookQualityRank ownedQuality, string? message, CancellationToken cancellationToken, string? ownedMediaQuality = null, int ownedMediaRevision = 1, int ownedFormatScore = 0) => throw new NotSupportedException();
        public Task<bool> TryCompleteSearchAsync(Guid id, IReadOnlyList<ScoredRelease> candidates, string? message, CancellationToken cancellationToken) =>
            TryTransitionStatusAsync(
                id,
                [AcquisitionStatus.Searching],
                AcquisitionStatus.AwaitingSelection,
                message,
                cancellationToken);
        public Task<AcquisitionQueueCandidate?> GetQueueCandidateAsync(Guid acquisitionId, Guid candidateId, CancellationToken cancellationToken) =>
            Task.FromResult(acquisitionId == AcquisitionId && QueueCandidate?.CandidateId == candidateId
                ? QueueCandidate
                : null);
        public Task<IReadOnlyList<AcquisitionCandidateRef>> ListAcceptedCandidatesAsync(Guid acquisitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkCandidatesBlocklistedAsync(Guid acquisitionId, string identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetSelectedReleaseAsync(Guid acquisitionId, SelectedRelease selected, CancellationToken cancellationToken) {
            SelectedRelease = selected;
            return Task.CompletedTask;
        }
        public Task<SelectedRelease?> GetSelectedReleaseAsync(Guid acquisitionId, CancellationToken cancellationToken) =>
            Task.FromResult(SelectedRelease);
        public Task<bool> BeginTransferAddAsync(
            Guid acquisitionId,
            Guid downloadClientConfigId,
            string correlation,
            string? category,
            TransferSeedGoal? seedGoal,
            CancellationToken cancellationToken) {
            BeginTransferAddCalls++;
            TransferPointer = new AcquisitionTransferInfo(
                Status ?? AcquisitionStatus.Queued,
                FinalSourcePath: null,
                correlation,
                downloadClientConfigId,
                category,
                TransferOwnershipState.Adding.ToCode());
            return Task.FromResult(true);
        }
        public Task<bool> CompleteTransferAddAsync(
            Guid acquisitionId,
            Guid downloadClientConfigId,
            string correlation,
            string clientItemId,
            SelectedRelease selectedRelease,
            string queuedMessage,
            CancellationToken cancellationToken) {
            BeforeCreateTransfer?.Invoke();
            CreateTransferCancellationToken = cancellationToken;
            if (CreateTransferResult) {
                SelectedRelease = selectedRelease;
                TransferPointer = new AcquisitionTransferInfo(
                    Status ?? AcquisitionStatus.Queued,
                    FinalSourcePath: null,
                    clientItemId,
                    downloadClientConfigId,
                    TransferPointer?.Category,
                    State: null);
            }
            return Task.FromResult(CreateTransferResult);
        }
        public Task AbandonTransferAddAsync(
            Guid acquisitionId,
            Guid downloadClientConfigId,
            string correlation,
            CancellationToken cancellationToken) {
            if (TransferPointer?.State == TransferOwnershipState.Adding.ToCode()
                && TransferPointer.DownloadClientConfigId == downloadClientConfigId
                && TransferPointer.ClientItemId == correlation) {
                TransferPointer = TransferPointer with {
                    ClientItemId = null,
                    DownloadClientConfigId = null,
                    Category = null,
                    State = null
                };
            }
            return Task.CompletedTask;
        }
        public Task<bool> CreateTransferAsync(Guid acquisitionId, Guid? downloadClientConfigId, string clientItemId, string? category, CancellationToken cancellationToken, TransferSeedGoal? seedGoal = null) {
            BeforeCreateTransfer?.Invoke();
            CreateTransferCancellationToken = cancellationToken;
            if (CreateTransferResult || PersistRejectedTransfer) {
                TransferPointer = new AcquisitionTransferInfo(
                    Status ?? AcquisitionStatus.Queued,
                    FinalSourcePath: null,
                    clientItemId,
                    downloadClientConfigId);
            }
            return Task.FromResult(CreateTransferResult);
        }
        public Task<IReadOnlyList<SeedingTransfer>> ListSeedingTransfersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkTransferSeedingAsync(Guid acquisitionId, DateTimeOffset since, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ClearTransferSeedingAsync(Guid transferId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActiveTransfer>> ListActiveTransfersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasActiveTransfersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateTransferAsync(Guid transferId, double progress, string? state, string? contentPath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkTransferStalledAsync(Guid transferId, DateTimeOffset? stalledSince, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AcquisitionImportContext?> GetImportContextAsync(Guid acquisitionId, CancellationToken cancellationToken) =>
            Task.FromResult(acquisitionId == AcquisitionId ? ImportContext : null);
        public Task<bool> TryClaimInitialImportAsync(Guid id, Guid claimJobId, bool allowManualRetry, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryHoldCorruptImportCheckpointAsync(Guid id, Guid claimJobId, string message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetFinalSourcePathAsync(Guid acquisitionId, string finalSourcePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetTvImportCheckpointAsync(Guid acquisitionId, TvImportCheckpoint? checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryCreateTvImportCheckpointAsync(Guid acquisitionId, TvImportCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryClaimTvImportCheckpointAsync(Guid acquisitionId, TvImportCheckpoint checkpoint, Guid claimJobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryClearTvImportCheckpointAsync(Guid acquisitionId, TvImportCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsCurrentTvImportCheckpointAsync(Guid acquisitionId, TvImportCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryCreateImportPlacementCheckpointAsync(Guid acquisitionId, ImportPlacementCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryAdvanceImportPlacementCheckpointAsync(Guid acquisitionId, ImportPlacementCheckpoint expected, ImportPlacementCheckpoint advanced, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryClaimImportPlacementCheckpointAsync(Guid acquisitionId, ImportPlacementCheckpoint checkpoint, Guid claimJobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryClearImportPlacementCheckpointAsync(Guid acquisitionId, ImportPlacementCheckpoint checkpoint, CancellationToken cancellationToken) {
            if (acquisitionId != AcquisitionId || ImportContext?.ImportPlacementCheckpoint != checkpoint) {
                return Task.FromResult(false);
            }

            ClearedPlacementCheckpoints++;
            ImportContext = ImportContext with { ImportPlacementCheckpoint = null };
            return Task.FromResult(true);
        }
        public Task<bool> IsCurrentImportPlacementCheckpointAsync(Guid acquisitionId, ImportPlacementCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task WriteImportHintAsync(Guid acquisitionId, string sourcePath, AcquisitionImportContext context, BookQualityRank ownedQuality, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> AnyOpenForEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AcquisitionDetail?> GetLatestForEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeDownloadClientConfigStore(bool includeRecordedClient) : IDownloadClientConfigStore {
        private static readonly DownloadClientDetail Default = new(
            DefaultClientId, DownloadClientKind.QBittorrent, "Default qBittorrent", "http://qbit", "admin", "prismedia", true, true, "secret");
        private static readonly DownloadClientDetail Recorded = new(
            RecordedClientId, DownloadClientKind.Transmission, "Recorded Transmission", "http://transmission", "user", "prismedia", true, true, "secret");

        public Task<DownloadClientDetail?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<DownloadClientDetail?>(includeRecordedClient && id == RecordedClientId ? Recorded : null);

        public Task<DownloadClientDetail?> GetDefaultAsync(CancellationToken cancellationToken) =>
            Task.FromResult<DownloadClientDetail?>(Default);

        public Task<IReadOnlyList<DownloadClientSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientDetail>> ListDetailsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientDetail?> GetDefaultAsync(DownloadProtocol protocol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientDetail>> ListEnabledAsync(DownloadProtocol protocol, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DownloadClientDetail>>(includeRecordedClient ? [Recorded] : [Default]);
        public Task<IReadOnlyList<DownloadProtocol>> GetEnabledProtocolsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientSummary> SaveAsync(DownloadClientSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingDownloadClientFactory : IDownloadClientFactory {
        private readonly RecordingDownloadClient _client = new();
        public List<(Guid ClientId, string ClientItemId, bool DeleteData)> Removals => _client.Removals;
        public IReadOnlyList<CancellationToken> RemovalCancellationTokens => _client.RemovalCancellationTokens;
        public int AddCount => _client.AddCount;
        public Exception? GetFailure { set => _client.GetFailure = value; }
        public Exception? RemoveFailure { get => _client.RemoveFailure; set => _client.RemoveFailure = value; }
        public bool ItemExists { set => _client.ItemExists = value; }
        public Action? OnAdd { get => _client.OnAdd; set => _client.OnAdd = value; }
        public List<DownloadItemStatus> Items => _client.Items;
        public IDownloadClient Get(DownloadClientKind kind) => _client;
    }

    private sealed class RecordingDownloadClient : IDownloadClient {
        public DownloadClientKind Kind => DownloadClientKind.QBittorrent;
        public List<(Guid ClientId, string ClientItemId, bool DeleteData)> Removals { get; } = [];
        public List<CancellationToken> RemovalCancellationTokens { get; } = [];
        public Exception? GetFailure { get; set; }
        public Exception? RemoveFailure { get; set; }
        public bool ItemExists { get; set; } = true;
        public Action? OnAdd { get; set; }
        public int AddCount { get; private set; }
        public List<DownloadItemStatus> Items { get; } = [];

        public Task RemoveAsync(DownloadClientConnection connection, string clientItemId, bool deleteData, CancellationToken cancellationToken) {
            Removals.Add((connection.Id, clientItemId, deleteData));
            RemovalCancellationTokens.Add(cancellationToken);
            if (RemoveFailure is not null) {
                throw RemoveFailure;
            }
            Items.RemoveAll(item => string.Equals(
                item.ClientItemId,
                clientItemId,
                StringComparison.OrdinalIgnoreCase));
            ItemExists = false;
            return Task.CompletedTask;
        }

        public Task<string> AddAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken cancellationToken) {
            AddCount++;
            OnAdd?.Invoke();
            ItemExists = true;
            return Task.FromResult("new-client-item");
        }
        public Task<string> AddTorrentFileAsync(DownloadClientConnection connection, string fileName, byte[] torrent, CancellationToken cancellationToken) =>
            AddAsync(connection, new DownloadAddRequest(fileName, null, connection.Category, fileName), cancellationToken);
        public Task<DownloadItemStatus?> GetItemAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
            if (GetFailure is not null) {
                throw GetFailure;
            }

            if (Items.Count > 0) {
                return Task.FromResult<DownloadItemStatus?>(Items.FirstOrDefault(item => string.Equals(
                    item.ClientItemId,
                    clientItemId,
                    StringComparison.OrdinalIgnoreCase)));
            }

            return Task.FromResult<DownloadItemStatus?>(ItemExists
                ? new DownloadItemStatus(clientItemId, "Book", 0.5, "downloading", false, "/save", "/save/book")
                : null);
        }
        public Task<IReadOnlyList<DownloadItemStatus>> ListItemsAsync(DownloadClientConnection connection, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DownloadItemStatus>>(Items.ToArray());
        public Task<IReadOnlyList<DownloadItemFile>> GetFilesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadItemProperties?> GetPropertiesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> GetPieceStatesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientConnectionTest> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAcquisitionHistoryStore : IAcquisitionHistoryStore {
        public List<AcquisitionHistoryEntry> Entries { get; } = [];
        public Task AddAsync(AcquisitionHistoryEntry entry, CancellationToken cancellationToken) {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AcquisitionHistoryView>> ListAsync(int limit, Guid? entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Minimal monitor-store fake recording retargets — the only member the service's delete path uses.</summary>
    private sealed class RecordingMonitorStore : IMonitorStore {
        public Guid MonitorId { get; } = Guid.NewGuid();
        public List<(Guid From, Guid To)> Retargets { get; } = [];
        public List<Guid> Deleted { get; } = [];
        public MonitorStatus? EntityMonitorStatus { get; set; }
        public bool RetargetSucceeds { get; set; } = true;

        public Task<bool> RetargetAsync(Guid fromAcquisitionId, Guid toAcquisitionId, CancellationToken cancellationToken) {
            Retargets.Add((fromAcquisitionId, toAcquisitionId));
            return Task.FromResult(RetargetSucceeds);
        }

        public Task<bool> RetargetAfterFileDeletionAsync(
            Guid fromAcquisitionId,
            Guid toAcquisitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<MonitorView> StartAsync(Guid acquisitionId, EntityKind kind, string title, string? author, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView> StartForEntityAsync(Guid entityId, EntityKind kind, string title, AcquisitionTargeting? targeting, MonitorPreset? preset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView?> GetByEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult<MonitorView?>(EntityMonitorStatus is { } status
                ? new MonitorView(
                    MonitorId, EntityKind.Book, null, status, "Book", null, null,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, entityId)
                : null);
        public Task<AcquisitionTargeting?> GetTargetingByEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorPreset?> GetPresetByEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid monitorId, CancellationToken cancellationToken) {
            Deleted.Add(monitorId);
            return Task.FromResult(true);
        }
        public Task<bool> SetStatusAsync(Guid monitorId, MonitorStatus status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MonitorView>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WantedPage> ListMissingAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WantedPage> ListCutoffUnmetAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView?> GetByAcquisitionAsync(Guid acquisitionId, CancellationToken cancellationToken) =>
            Task.FromResult<MonitorView?>(acquisitionId == AcquisitionId
                ? new MonitorView(
                    MonitorId, EntityKind.Book, AcquisitionId, MonitorStatus.Fulfilled, "Dune", "Frank Herbert",
                    AcquisitionStatus.Imported, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, WantedEntityId)
                : null);
        public Task<bool> HasActiveMonitorsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DueMonitor>> ListDueMonitorsAsync(int defaultIntervalMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkSearchedAsync(Guid monitorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> CreateUpgradeChildAsync(Guid monitorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResolveUpgradeChildAsync(Guid childId, bool succeeded, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptyImportedFilesReader : IImportedFilesReader {
        public IReadOnlyList<DownloadItemFile> List(string path) => [];
    }

    private sealed class NullAcquisitionProfileStore : IBookAcquisitionProfileStore {
        public Task<string?> GetDownloadCategoryAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
        public Task<BookAcquisitionRules> GetRulesAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BookImportProfile?> GetImportProfileAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GetAutoPickAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GetAutoRedownloadAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookAcquisitionProfileView>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BookAcquisitionProfileView?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BookAcquisitionProfileView> SaveAsync(BookAcquisitionProfileSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NullIndexerConfigStore : IIndexerConfigStore {
        public Task<IndexerConfigDetail?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<IndexerConfigDetail?>(null);
        public Task<IReadOnlyList<IndexerConfigSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IndexerConfigDetail>> ListDetailsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IndexerConfigSummary> SaveAsync(IndexerConfigSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NullReleaseLinkResolver : IReleaseLinkResolver {
        public Task<string?> ResolveMagnetAsync(string infoUrl, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class ThrowingBlocklistStore : IAcquisitionBlocklistStore {
        public Task<IReadOnlySet<string>> GetIdentitiesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        public Task<IReadOnlyList<AcquisitionBlocklistEntry>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(BlocklistAddRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Requests { get; } = [];
        public Exception? EnqueueFailure { get; set; }
        public Action? BeforeEnqueue { get; set; }

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            BeforeEnqueue?.Invoke();
            if (EnqueueFailure is not null) {
                throw EnqueueFailure;
            }

            Requests.Add(request);
            return Task.FromResult(new JobRunSnapshot(
                Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null,
                request.PayloadJson ?? "{}", null, request.TargetEntityId, request.TargetLabel,
                DateTimeOffset.UtcNow, null, null));
        }
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(Requests.Any(request => request.Type == type && request.TargetEntityId == targetEntityId));
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) => throw new NotSupportedException();
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
