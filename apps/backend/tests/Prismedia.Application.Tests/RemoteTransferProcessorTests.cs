using System.Reflection;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class RemoteTransferProcessorTests {
    [Fact]
    public async Task UnacceptedAmbiguousCancellationUsesAtomicFenceAndNeverPostsANewJob() {
        var fixture = new Fixture();
        fixture.MarkSubmissionUncertain();
        fixture.RequestCancellation();
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Cancelled, fixture.State.Phase);
        Assert.Equal(0, fixture.Submissions);
        Assert.Equal(0, fixture.Cancellations);
        Assert.Equal(0, fixture.Downloads);
    }

    [Fact]
    public async Task CancellationOfAmbiguousSubmissionRecoversAndStopsTheOriginalJobWithoutImport() {
        var fixture = new Fixture { LoseSubmissionOnce = true };
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        fixture.RequestCancellation();
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Cancelled, fixture.State.Phase);
        Assert.Equal(RemoteJobState.Succeeded, fixture.State.LastRemoteState);
        Assert.Equal(1, fixture.Submissions);
        Assert.Equal(1, fixture.Cancellations);
        Assert.Equal(0, fixture.Downloads);
        Assert.Empty(fixture.Receipts);
    }

    [Fact]
    public async Task CancellationKeepsOwnershipWhileRemoteExecutionIsStillRunning() {
        var fixture = new Fixture { RemoteState = RemoteJobState.Running };
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        fixture.RequestCancellation();
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.NotEqual(IntegrationTransferPhase.Cancelled, fixture.State.Phase);
        fixture.RemoteState = RemoteJobState.Cancelled;
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Cancelled, fixture.State.Phase);
        Assert.Equal(2, fixture.Cancellations);
        Assert.Equal(0, fixture.Downloads);
    }

    [Fact]
    public async Task LostSubmissionResponseRecoversOriginalJobAfterRestartWithoutAnotherPost() {
        var fixture = new Fixture { LoseSubmissionOnce = true };
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.SubmissionUncertain, fixture.State.Phase);
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(1, fixture.Submissions);
        Assert.Equal(2, fixture.Lookups);
        Assert.Equal(1, fixture.Downloads);
        Assert.Equal(1, fixture.Materializations);
    }

    [Fact]
    public async Task WaitingDefersTheSameOperationWithoutDownloadingOrImporting() {
        var fixture = new Fixture { RemoteState = RemoteJobState.Waiting };
        var wait = await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.InRange(wait.RetryDelay.TotalSeconds, 5, 3600);
        Assert.Equal(IntegrationTransferPhase.AwaitingRemote, fixture.State.Phase);
        Assert.Equal(0, fixture.Downloads);
        fixture.RemoteState = RemoteJobState.Succeeded;
        await fixture.RunAsync();
        Assert.Equal(1, fixture.Submissions);
        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
    }

    [Fact]
    public async Task LostAcknowledgementResponseRetriesOnlyOriginalReceipt() {
        var fixture = new Fixture { LoseAcknowledgementOnce = true };
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.AwaitingAcknowledgement, fixture.State.Phase);
        var receipt = fixture.State.ReceiptId;
        await fixture.RunAsync();
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(2, fixture.Receipts.Count);
        Assert.All(fixture.Receipts, accepted => Assert.Equal(receipt, accepted));
        Assert.Equal(1, fixture.Downloads);
        Assert.Equal(1, fixture.Materializations);
        Assert.Equal(1, fixture.Placements);
    }

    [Fact]
    public async Task LostImportCommitUsesLocalEvidenceAndReconcilesExactExistingEntities() {
        var fixture = new Fixture { FailImportSaveOnce = true };
        await Assert.ThrowsAsync<IntegrationInvocationException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.Importing, fixture.State.Phase);
        Assert.Empty(fixture.Receipts);
        await fixture.RunAsync();
        Assert.Equal(1, fixture.Downloads);
        Assert.Equal(2, fixture.Materializations);
        Assert.Equal(fixture.EntityId, Assert.Single(Assert.Single(fixture.State.Imports!).EntityIds));
    }

    [Theory]
    [InlineData(EntityKind.Book)]
    [InlineData(EntityKind.ComicInstallment)]
    public async Task MissingStagingAfterSinglePlacementRecoversWithoutRemoteRedownload(EntityKind kind) {
        var fixture = new Fixture(kind) { MissingStaging = true, PlacedRecoveryAvailable = true };
        fixture.PrepareImport();

        await fixture.RunAsync();

        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(0, fixture.Downloads);
        Assert.Equal(0, fixture.ArtifactAuthorizations);
        Assert.Equal(1, fixture.PlacedReads);
        Assert.Equal(1, fixture.Materializations);
    }

    [Fact]
    public async Task VerifiedLocalReceiptFinishesImportWhileExecutorRetentionIsUnavailable() {
        var fixture = new Fixture { RetentionUnavailable = true };
        fixture.StageArtifactBeforeVerificationCommit();
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(0, fixture.Downloads);
        Assert.Equal(0, fixture.ArtifactAuthorizations);
        Assert.Equal(2, fixture.LocalReads);
        Assert.Equal(2, fixture.Renewals);
        Assert.Equal(2, fixture.Verifications);
        Assert.Equal(1, fixture.Materializations);
        Assert.Single(fixture.Receipts);
    }

    [Fact]
    public async Task AcknowledgementContinuesWhenRetentionRenewalIsUnavailable() {
        var fixture = new Fixture { RetentionUnavailable = true };
        fixture.PrepareAwaitingAcknowledgement();
        var receipt = fixture.State.ReceiptId;
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(1, fixture.Renewals);
        Assert.Equal(receipt, Assert.Single(fixture.Receipts));
    }

    [Fact]
    public async Task VerifiedLocalReceiptCommitsImportWhileExecutorIsUnavailable() {
        var fixture = new Fixture { ExecutorUnavailable = true };
        fixture.StageArtifactBeforeVerificationCommit();
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.AwaitingAcknowledgement, fixture.State.Phase);
        Assert.Equal(0, fixture.Downloads);
        Assert.Equal(0, fixture.ArtifactAuthorizations);
        Assert.Equal(1, fixture.Materializations);
        Assert.Empty(fixture.Receipts);
    }

    [Fact]
    public async Task UnexpectedRetentionFailureStopsBeforeLocalImport() {
        var fixture = new Fixture { UnexpectedRetentionFailure = true };
        fixture.PrepareImport();
        await Assert.ThrowsAsync<IntegrationInvocationException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.Importing, fixture.State.Phase);
        Assert.Equal(0, fixture.Materializations);
        Assert.Empty(fixture.Receipts);
    }

    [Theory]
    [InlineData(RemoteJobState.Partial, false)]
    [InlineData(RemoteJobState.Succeeded, true)]
    public async Task PartialOrExpiredOutputsRemainReviewableWithoutInventingAvailability(RemoteJobState state, bool expired) {
        var fixture = new Fixture { RemoteState = state, Expired = expired };
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.NeedsReview, fixture.State.Phase);
        Assert.Equal(0, fixture.Downloads);
        Assert.Empty(fixture.Receipts);
        if (!expired) {
            Assert.Equal(1, fixture.Renewals);
            await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
            Assert.Equal(2, fixture.Renewals);
        }
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task UnrelatedJobOrManifestEvidenceNeverAuthorizesBytes(bool wrongInstance, bool wrongOperation, bool wrongManifest) {
        var fixture = new Fixture { WrongInstance = wrongInstance, WrongOperation = wrongOperation, WrongManifest = wrongManifest };
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.Equal(0, fixture.Downloads);
        Assert.Equal(0, fixture.Materializations);
    }

    [Fact]
    public async Task ManifestCannotSmuggleAnUnselectedItemOrUnimportedSidecarIntoAReceipt() {
        var fixture = new Fixture { WrongArtifactItem = true };
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.NeedsReview, fixture.State.Phase);
        Assert.Equal(0, fixture.Downloads);
        Assert.Empty(fixture.Receipts);
    }

    [Fact]
    public async Task ImageOutputIsVerifiedImportedAndAcknowledgedAsAnImage() {
        var fixture = new Fixture(EntityKind.Image);
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(1, fixture.Downloads);
        Assert.Equal(1, fixture.Materializations);
        Assert.Single(fixture.Receipts);
    }

    [Fact]
    public async Task GalleryImportsEveryExactImageBeforeAcknowledgingAndRetainsItsContainerAcrossReceiptRetry() {
        var fixture = new Fixture(EntityKind.Gallery) { LoseAcknowledgementOnce = true };
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.AwaitingAcknowledgement, fixture.State.Phase);
        Assert.Equal(2, fixture.State.Imports!.Count);
        Assert.All(fixture.State.Imports, receipt => Assert.Equal(fixture.EntityId, receipt.ContainerEntityId));
        Assert.Equal(2, fixture.State.Imports.SelectMany(receipt => receipt.EntityIds).Distinct().Count());
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(2, fixture.Downloads);
        Assert.Equal(1, fixture.Placements);
        Assert.Equal(1, fixture.Materializations);
        Assert.Equal(2, fixture.Receipts.Count);
        Assert.Single(fixture.Receipts.Distinct());
        Assert.Contains(IntegrationTransferPhase.Importing, fixture.RetentionPhases);
        Assert.Equal(2, fixture.RetentionPhases.Count(phase => phase == IntegrationTransferPhase.AwaitingAcknowledgement));
    }
    [Fact]
    public async Task GalleryRecoversWhenSomeImportReceiptsWereCommittedBeforeACrash() {
        var fixture = new Fixture(EntityKind.Gallery) { FailImportSaveOnce = true };
        await Assert.ThrowsAsync<IntegrationInvocationException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.Importing, fixture.State.Phase);
        var first = Assert.Single(fixture.State.Imports!);
        Assert.Empty(fixture.Receipts);
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(first, fixture.State.Imports![0]);
        Assert.Equal(2, fixture.Downloads);
        Assert.Equal(2, fixture.Materializations);
    }
    [Fact]
    public async Task MissingGalleryStagingRecoversExactPublishedGroupWithoutRemoteRedownload() {
        var fixture = new Fixture(EntityKind.Gallery) { MissingStaging = true, PlacedRecoveryAvailable = true };
        fixture.PrepareImport();

        await fixture.RunAsync();

        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(0, fixture.Downloads);
        Assert.Equal(0, fixture.ArtifactAuthorizations);
        Assert.Equal(1, fixture.PlacedReads);
        Assert.Equal(1, fixture.Materializations);
    }
    [Fact]
    public async Task GalleryDoesNotAcknowledgeImagesOwnedOutsideItsContainer() {
        var fixture = new Fixture(EntityKind.Gallery) { WrongImageParent = true };
        await Assert.ThrowsAsync<IntegrationInvocationException>(fixture.RunAsync);
        Assert.Empty(fixture.State.Imports!);
        Assert.Empty(fixture.Receipts);
    }
    [Fact]
    public async Task GalleryWithAnOrderGapIsRetainedForReviewBeforeDownloading() {
        var fixture = new Fixture(EntityKind.Gallery) { GalleryOrderGap = true };
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.NeedsReview, fixture.State.Phase);
        Assert.Equal(0, fixture.Downloads);
        Assert.Empty(fixture.Receipts);
    }
    private sealed class Fixture {
        private const string PluginId = "executor-fixture";
        private const string InstanceId = "fixture-installation";
        private const string JobId = "fixture-job";
        private const string ManifestRevision = "manifest-1";
        private const string ItemId = "publication";
        private static readonly string Hash = new('a', 64);
        internal IntegrationTransferState State { get; private set; }
        internal RemoteJobState RemoteState { get; set; } = RemoteJobState.Succeeded;
        internal bool LoseSubmissionOnce { get; set; }
        internal bool LoseAcknowledgementOnce { get; set; }
        internal bool FailImportSaveOnce { get; set; }
        internal bool Expired { get; set; }
        internal bool WrongInstance { get; set; }
        internal bool WrongOperation { get; set; }
        internal bool WrongManifest { get; set; }
        internal bool WrongArtifactItem { get; set; }
        internal bool WrongImageParent { get; set; }
        internal bool GalleryOrderGap { get; set; }
        internal bool RetentionUnavailable { get; set; }
        internal bool ExecutorUnavailable { get; set; }
        internal bool UnexpectedRetentionFailure { get; set; }
        internal bool MissingStaging { get; set; }
        internal bool PlacedRecoveryAvailable { get; set; }
        private bool localReceiptBeforeVerificationCommit;
        private readonly Guid[] imageIds = [Guid.NewGuid(), Guid.NewGuid()];
        internal int Submissions { get; private set; }
        internal int Lookups { get; private set; }
        internal int Downloads { get; private set; }
        internal int Materializations { get; private set; }
        internal int Placements { get; private set; }
        internal int Renewals { get; private set; }
        internal int ArtifactAuthorizations { get; private set; }
        internal int LocalReads { get; private set; }
        internal int Verifications { get; private set; }
        internal int Cancellations { get; private set; }
        internal int PlacedReads { get; private set; }
        internal Guid EntityId { get; } = Guid.NewGuid();
        internal List<Guid> Receipts { get; } = [];
        internal List<IntegrationTransferPhase> RetentionPhases { get; } = [];
        private bool accepted;
        private long remoteRevision;
        private readonly LibraryRootData root = new(Guid.NewGuid(), Path.GetTempPath(), "Library", true, false, false, false, false, true, false, false);
        private readonly IntegrationConnection connection;
        private readonly PluginManifest manifest;
        private readonly IntegrationTransferPlan plan;
        private IntegrationArtifact Artifact => new("artifact", WrongArtifactItem ? "unselected-item" : ItemId, "nested/" + FileName,
            kind == EntityKind.ComicInstallment ? "application/vnd.comicbook+zip" : "application/epub+zip", 100, Hash, IntegrationArtifactRole.Content);
        private IReadOnlyList<IntegrationArtifact> Artifacts => kind == EntityKind.Gallery
            ? Enumerable.Range(1, 2).Select(index => Artifact with { Id = "image-" + index, RelativePath = "nested/" + index + ".png", GroupId = "group", Ordinal = index + (GalleryOrderGap ? 1 : 0) }).ToArray()
            : [Artifact];
        private RemoteTransferSnapshot Snapshot => new(WrongInstance ? "another-installation" : InstanceId, JobId,
            WrongOperation ? Guid.NewGuid() : State.OperationId, ++remoteRevision, RemoteState, 0.5,
            RemoteState is RemoteJobState.Succeeded or RemoteJobState.Partial ? ManifestRevision : null,
            DateTimeOffset.UtcNow.AddHours(1), [], NextPollAfter: DateTimeOffset.UtcNow.AddSeconds(20), ArtifactsExpired: Expired);
        private readonly EntityKind kind;
        private string FileName => kind switch { EntityKind.Image or EntityKind.Gallery => "image.png", EntityKind.ComicInstallment => "comic.cbz", _ => "book.epub" };
        internal Fixture(EntityKind kind = EntityKind.Book) {
            this.kind = kind;
            var supports = new IntegrationSupport[] {
                new(PluginCapability.TransferExecutor, [IntegrationOperation.Submit, IntegrationOperation.FindSubmission,
                    IntegrationOperation.GetJob, IntegrationOperation.ListArtifacts, IntegrationOperation.AuthorizeArtifact,
                    IntegrationOperation.RenewRetention, IntegrationOperation.Acknowledge, IntegrationOperation.Cancel, IntegrationOperation.CancelSubmission], [kind])
            };
            connection = IntegrationConnection.Create(PluginId, "Executor", "http://executor.test/", true, [PluginCapability.TransferExecutor], new Dictionary<string, string>());
            connection.RecordProbe(InstanceId, supports, null, DateTimeOffset.UtcNow);
            manifest = new(2, [], PluginId, "Executor", "1.0.0", "dotnet-process", "plugin.dll", new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, supports.Select(s => new PluginIntegrationCapability(s.Kind, s.Operations, s.EntityKinds)).ToArray(), []));
            State = IntegrationTransfer.Create(Guid.NewGuid(), connection.State.Id, InstanceId).State;
            plan = new("Publication", kind, root.Id, root.Path, "owner", Hash,
                Executor: new(State.OperationId, "http://source.test/book", "selection", "selection-1", [ItemId], 1, 1000));
        }
        internal Task RunAsync() {
            var store = Proxy<IIntegrationTransferStore>((method, args) => method switch {
                nameof(IIntegrationTransferStore.FindAsync) => Task.FromResult<StoredIntegrationTransfer?>(new(new(State), plan, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)),
                nameof(IIntegrationTransferStore.SaveAsync) => Save((IntegrationTransfer)args![0]!, (long)args[1]!),
                nameof(IIntegrationTransferStore.RecordErrorAsync) => Task.CompletedTask,
                _ => throw new NotSupportedException(method)
            });
            var connections = Proxy<IIntegrationConnectionStore>((method, _) => method switch {
                nameof(IIntegrationConnectionStore.FindAsync) => Task.FromResult<StoredIntegrationConnection?>(new(connection, [])),
                nameof(IIntegrationConnectionStore.ReadSecretsAsync) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()),
                _ => throw new NotSupportedException(method)
            });
            var plugins = Proxy<IIntegrationPluginGateway>((method, _) => method == nameof(IIntegrationPluginGateway.FindAsync)
                ? Task.FromResult<PluginManifest?>(ExecutorUnavailable ? null : manifest)
                : throw new NotSupportedException(method));
            var gateway = Proxy<IIntegrationTransferGateway>((method, args) => method switch {
                nameof(IIntegrationTransferGateway.FindSubmissionAsync) => Find(),
                nameof(IIntegrationTransferGateway.CancelSubmissionAsync) => Task.FromResult(new CancelSubmissionResult(InstanceId, State.OperationId, true, null)),
                nameof(IIntegrationTransferGateway.SubmitAsync) => Submit((SubmitTransferInput)args![2]!),
                nameof(IIntegrationTransferGateway.GetJobAsync) => Task.FromResult(Snapshot),
                nameof(IIntegrationTransferGateway.CancelAsync) => Cancel(),
                nameof(IIntegrationTransferGateway.RenewRetentionAsync) => Renew((RenewTransferRetentionInput)args![2]!),
                nameof(IIntegrationTransferGateway.ReadManifestAsync) => Task.FromResult(new TransferManifestPage(JobId, WrongManifest ? "another-revision" : ManifestRevision, true, Artifacts.Count, Artifacts)),
                nameof(IIntegrationTransferGateway.AuthorizeArtifactAsync) => Authorize(),
                nameof(IIntegrationTransferGateway.AcknowledgeAsync) => Acknowledge((AcknowledgeTransferInput)args![2]!),
                _ => throw new NotSupportedException(method)
            });
            var bytes = Proxy<IIntegrationArtifactTransfer>((method, args) => method switch {
                nameof(IIntegrationArtifactTransfer.TransferAsync) => (object)Download((IntegrationArtifactTransferRequest)args![0]!),
                nameof(IIntegrationArtifactTransfer.ReadVerifiedAsync) => ReadVerified((string)args![1]!, (string)args[2]!, (long)args[3]!, (string)args[4]!),
                _ => throw new NotSupportedException(method)
            });
            var verifier = Proxy<IIntegrationMediaVerifier>((_, args) => {
                Assert.Equal(kind == EntityKind.Gallery ? EntityKind.Image : kind, args![1]);
                Verifications++;
                return Task.CompletedTask;
            });
            var placement = Proxy<IIntegrationImportPlacement>((method, args) => method switch {
                nameof(IIntegrationImportPlacement.PlaceAsync) => PlaceSingle(),
                nameof(IIntegrationImportPlacement.ReadPlacedAsync) => ReadPlaced((IntegrationArtifact)args![3]!),
                _ => throw new NotSupportedException(method)
            });
            var roots = Proxy<ILibraryScanRootPersistence>((_, _) => Task.FromResult<LibraryRootData?>(root));
            var materializer = Proxy<IImportedEntityMaterializer>((_, args) => {
                Assert.Equal(kind, args![0]);
                Materializations++;
                var request = (ImportedEntityMaterializationRequest)args![2]!;
                return Task.FromResult(kind == EntityKind.Gallery
                    ? new ImportedEntityMaterializationResult(imageIds.Select(id => new ImportedEntityReference(id, EntityKind.Image)).ToArray(), [EntityId], request.PlacedMediaPaths, [], [], [new(EntityId, EntityKind.Gallery)])
                    : new ImportedEntityMaterializationResult([new(EntityId, kind)], [], request.PlacedMediaPaths, [], []));
            });
            var galleryPlacement = Proxy<IIntegrationGalleryPlacement>((method, args) => {
                var outputs = method == nameof(IIntegrationGalleryPlacement.PlaceAsync)
                    ? ((IntegrationGalleryPlacementRequest)args![0]!).Outputs
                    : (IntegrationGalleryOutputSet)args![3]!;
                if (method == nameof(IIntegrationGalleryPlacement.ReadPlacedAsync)) {
                    PlacedReads++;
                    return Task.FromResult<PlacedIntegrationGallery?>(PlacedRecoveryAvailable ? GalleryResult(outputs) : null);
                }
                Placements++;
                return Task.FromResult(GalleryResult(outputs));
            });
            var readiness = Proxy<IImportedEntityReadinessPersistence>((_, args) => {
                var path = ((IReadOnlyCollection<string>)args![0]!).Single();
                var index = Array.FindIndex(Artifacts.ToArray(), artifact => Path.GetFileNameWithoutExtension(path) == artifact.Id);
                return Task.FromResult(new ImportedEntityReadyScope([new(imageIds[index], EntityKind.Image)], WrongImageParent ? [] : [EntityId]));
            });
            var galleries = new IntegrationGalleryImporter(bytes, verifier, galleryPlacement, roots, materializer, readiness);
            var job = new JobRunSnapshot(Guid.NewGuid(), JobType.IntegrationTransfer, JobRunStatus.Running, 0, null, "{}",
                JobTargetKinds.IntegrationTransfer, State.OperationId.ToString(), "Publication", DateTimeOffset.UtcNow, null, null);
            var queue = Proxy<IJobQueueService>((method, _) => method switch {
                nameof(IJobQueueService.UpdateProgressAsync) => Task.CompletedTask,
                nameof(IJobQueueService.IsRunCancelledAsync) or nameof(IJobQueueService.HasPendingAsync) => Task.FromResult(false),
                nameof(IJobQueueService.EnqueueChildAsync) => Task.FromResult(job),
                _ => throw new NotSupportedException(method)
            });
            return new RemoteTransferProcessor(store, new(connections, plugins), gateway, new(gateway), bytes, verifier, placement, roots, materializer, galleries)
                .ProcessAsync(State.OperationId, new(job, queue), default);
        }
        private Task Save(IntegrationTransfer transfer, long expectedRevision) {
            Assert.Equal(State.Revision, expectedRevision);
            if (FailImportSaveOnce && transfer.State.Phase == IntegrationTransferPhase.AwaitingAcknowledgement) {
                FailImportSaveOnce = false; throw new IOException("Simulated crash after materialization");
            }
            State = transfer.State;
            return Task.CompletedTask;
        }
        internal void RequestCancellation() {
            var transfer = new IntegrationTransfer(State);
            transfer.RequestRemoteCancellation();
            State = transfer.State;
        }
        internal void MarkSubmissionUncertain() {
            var transfer = new IntegrationTransfer(State); transfer.BeginSubmission(); State = transfer.State;
        }
        internal void StageArtifactBeforeVerificationCommit() {
            var transfer = new IntegrationTransfer(State);
            transfer.BeginSubmission(); transfer.AcceptSubmission(State.OperationId, InstanceId, JobId);
            transfer.Observe(InstanceId, JobId, 1, RemoteJobState.Succeeded, ManifestRevision);
            transfer.AcceptManifest(new(JobId, ManifestRevision, true, Artifacts.Count, Artifacts));
            State = transfer.State;
            localReceiptBeforeVerificationCommit = true;
        }
        internal void PrepareAwaitingAcknowledgement() {
            PrepareImport();
            var transfer = new IntegrationTransfer(State);
            transfer.RecordImported(new(Artifact.Id, Artifact.Sha256, [EntityId]));
            State = transfer.State;
        }
        internal void PrepareImport() {
            StageArtifactBeforeVerificationCommit();
            var transfer = new IntegrationTransfer(State);
            foreach (var artifact in Artifacts) transfer.RecordVerified(artifact.Id, artifact.SizeBytes, artifact.Sha256);
            State = transfer.State;
        }
        private Task<RemoteTransferSnapshot> Cancel() { Cancellations++; return Task.FromResult(Snapshot); }
        private Task<FindTransferResult> Find() { Lookups++; return Task.FromResult(new FindTransferResult(accepted ? Snapshot : null)); }
        private Task<RemoteTransferSnapshot> Submit(SubmitTransferInput input) {
            Assert.Equal(IntegrationTransferPhase.SubmissionUncertain, State.Phase);
            Assert.Equal(State.OperationId, input.ClientOperationId);
            Submissions++; accepted = true;
            if (LoseSubmissionOnce) { LoseSubmissionOnce = false; throw new IntegrationInvocationException("Response lost"); }
            return Task.FromResult(Snapshot);
        }
        private Task<TransferRetentionResult> Renew(RenewTransferRetentionInput input) {
            Renewals++;
            RetentionPhases.Add(State.Phase);
            if (UnexpectedRetentionFailure) return Task.FromException<TransferRetentionResult>(new InvalidOperationException("Unexpected renewal failure"));
            return RetentionUnavailable
                ? Task.FromException<TransferRetentionResult>(new IntegrationInvocationException("Retention unavailable"))
                : Task.FromResult(new TransferRetentionResult(input.JobId, input.RetainUntil));
        }
        private Task<HttpArtifactDelivery> Authorize() {
            ArtifactAuthorizations++;
            return Task.FromResult(new HttpArtifactDelivery("http://executor.test/file", new Dictionary<string, string>(), FileName, ByteSize: 100, Sha256: Hash));
        }
        private Task<VerifiedIntegrationArtifact?> ReadVerified(string artifactId, string fileName, long sizeBytes, string sha256) {
            LocalReads++;
            var accepted = Artifacts.Single(artifact => artifact.Id == artifactId);
            Assert.Equal(Path.GetFileName(accepted.RelativePath), fileName);
            Assert.Equal(accepted.SizeBytes, sizeBytes);
            Assert.Equal(accepted.Sha256, sha256);
            var available = !MissingStaging && (localReceiptBeforeVerificationCommit || State.VerifiedArtifactIds?.Contains(artifactId) == true);
            return Task.FromResult<VerifiedIntegrationArtifact?>(available
                ? new(artifactId, Path.Combine(root.Path, "staged.epub"), 100, Hash, fileName)
                : null);
        }
        private Task<string> PlaceSingle() { Placements++; return Task.FromResult(Path.Combine(root.Path, "placed" + Path.GetExtension(FileName))); }
        private Task<VerifiedIntegrationArtifact?> ReadPlaced(IntegrationArtifact artifact) {
            PlacedReads++;
            return Task.FromResult<VerifiedIntegrationArtifact?>(PlacedRecoveryAvailable
                ? new(artifact.Id, Path.Combine(root.Path, "placed" + Path.GetExtension(artifact.RelativePath)), artifact.SizeBytes,
                    artifact.Sha256, Path.GetFileName(artifact.RelativePath))
                : null);
        }
        private PlacedIntegrationGallery GalleryResult(IntegrationGalleryOutputSet outputs) =>
            new(Path.Combine(root.Path, "gallery"), outputs.Artifacts.ToDictionary(artifact => artifact.Id,
                artifact => Path.Combine(root.Path, "gallery", artifact.Id + ".png")));
        private Task<VerifiedIntegrationArtifact> Download(IntegrationArtifactTransferRequest request) {
            var artifact = Artifacts.Single(artifact => artifact.Id == request.ArtifactId);
            Assert.Equal(Path.GetFileName(artifact.RelativePath), request.Delivery.SuggestedFileName);
            Downloads++; return Task.FromResult(new VerifiedIntegrationArtifact(artifact.Id, Path.Combine(root.Path, "staged.epub"), 100, Hash, Path.GetFileName(artifact.RelativePath)));
        }
        private Task<TransferAcknowledgement> Acknowledge(AcknowledgeTransferInput input) {
            Assert.Equal(IntegrationTransferPhase.AwaitingAcknowledgement, State.Phase);
            Assert.Equal(State.ReceiptId, input.ReceiptId);
            if (kind == EntityKind.Gallery) {
                Assert.Equal(2, input.Artifacts.Count);
                Assert.All(input.Artifacts, artifact => Assert.Equal(EntityId, artifact.ContainerEntityId));
                Assert.Equal(imageIds.Order(), input.Artifacts.SelectMany(artifact => artifact.EntityIds).Order());
            } else Assert.Equal(EntityId, Assert.Single(Assert.Single(input.Artifacts).EntityIds));
            Receipts.Add(input.ReceiptId);
            if (LoseAcknowledgementOnce) { LoseAcknowledgementOnce = false; throw new IntegrationInvocationException("Receipt response lost"); }
            return Task.FromResult(new TransferAcknowledgement(input.JobId, input.ReceiptId, true));
        }
    }
    private static T Proxy<T>(Func<string, object?[]?, object?> call) where T : class {
        var result = DispatchProxy.Create<T, Boundary>();
        ((Boundary)(object)result).Call = call;
        return result;
    }
    public class Boundary : DispatchProxy {
        public Func<string, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args);
    }
}
