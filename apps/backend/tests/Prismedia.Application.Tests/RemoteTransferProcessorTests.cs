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
        internal int Submissions { get; private set; }
        internal int Lookups { get; private set; }
        internal int Downloads { get; private set; }
        internal int Materializations { get; private set; }
        internal int Placements { get; private set; }
        internal int Renewals { get; private set; }
        internal Guid EntityId { get; } = Guid.NewGuid();
        internal List<Guid> Receipts { get; } = [];
        private bool accepted;
        private long remoteRevision;
        private readonly LibraryRootData root = new(Guid.NewGuid(), Path.GetTempPath(), "Library", true, false, false, false, false, true, false, false);
        private readonly IntegrationConnection connection;
        private readonly PluginManifest manifest;
        private readonly IntegrationTransferPlan plan;
        private IntegrationArtifact Artifact => new("artifact", WrongArtifactItem ? "unselected-item" : ItemId, "nested/book.epub", "application/epub+zip", 100, Hash, IntegrationArtifactRole.Content);
        private RemoteTransferSnapshot Snapshot => new(WrongInstance ? "another-installation" : InstanceId, JobId,
            WrongOperation ? Guid.NewGuid() : State.OperationId, ++remoteRevision, RemoteState, 0.5,
            RemoteState is RemoteJobState.Succeeded or RemoteJobState.Partial ? ManifestRevision : null,
            DateTimeOffset.UtcNow.AddHours(1), [], NextPollAfter: DateTimeOffset.UtcNow.AddSeconds(20), ArtifactsExpired: Expired);
        internal Fixture() {
            var supports = new IntegrationSupport[] {
                new(PluginCapability.TransferExecutor, [IntegrationOperation.Submit, IntegrationOperation.FindSubmission,
                    IntegrationOperation.GetJob, IntegrationOperation.ListArtifacts, IntegrationOperation.AuthorizeArtifact,
                    IntegrationOperation.RenewRetention, IntegrationOperation.Acknowledge], [EntityKind.Book])
            };
            connection = IntegrationConnection.Create(PluginId, "Executor", "http://executor.test/", true, [PluginCapability.TransferExecutor], new Dictionary<string, string>());
            connection.RecordProbe(InstanceId, supports, null, DateTimeOffset.UtcNow);
            manifest = new(2, [], PluginId, "Executor", "1.0.0", "dotnet-process", "plugin.dll", new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, supports.Select(s => new PluginIntegrationCapability(s.Kind, s.Operations, s.EntityKinds)).ToArray(), []));
            State = IntegrationTransfer.Create(Guid.NewGuid(), connection.State.Id, InstanceId).State;
            plan = new("Publication", EntityKind.Book, root.Id, root.Path, "owner", Hash,
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
            var plugins = Proxy<IIntegrationPluginGateway>((method, _) => method == nameof(IIntegrationPluginGateway.FindAsync) ? Task.FromResult<PluginManifest?>(manifest) : throw new NotSupportedException(method));
            var gateway = Proxy<IIntegrationTransferGateway>((method, args) => method switch {
                nameof(IIntegrationTransferGateway.FindSubmissionAsync) => Find(),
                nameof(IIntegrationTransferGateway.SubmitAsync) => Submit((SubmitTransferInput)args![2]!),
                nameof(IIntegrationTransferGateway.GetJobAsync) => Task.FromResult(Snapshot),
                nameof(IIntegrationTransferGateway.RenewRetentionAsync) => Renew((RenewTransferRetentionInput)args![2]!),
                nameof(IIntegrationTransferGateway.ReadManifestAsync) => Task.FromResult(new TransferManifestPage(JobId, WrongManifest ? "another-revision" : ManifestRevision, true, 1, [Artifact])),
                nameof(IIntegrationTransferGateway.AuthorizeArtifactAsync) => Task.FromResult(new HttpArtifactDelivery("http://executor.test/file", new Dictionary<string, string>(), "book.epub", ByteSize: 100, Sha256: Hash)),
                nameof(IIntegrationTransferGateway.AcknowledgeAsync) => Acknowledge((AcknowledgeTransferInput)args![2]!),
                _ => throw new NotSupportedException(method)
            });
            var bytes = Proxy<IIntegrationArtifactTransfer>((method, args) => method switch {
                nameof(IIntegrationArtifactTransfer.TransferAsync) => (object)Download((IntegrationArtifactTransferRequest)args![0]!),
                nameof(IIntegrationArtifactTransfer.ReadVerifiedAsync) => Task.FromResult<VerifiedIntegrationArtifact?>(new(Artifact.Id, Path.Combine(root.Path, "staged.epub"), 100, Hash, "book.epub")),
                _ => throw new NotSupportedException(method)
            });
            var verifier = Proxy<IIntegrationPublicationVerifier>((_, _) => Task.CompletedTask);
            var placement = Proxy<IIntegrationImportPlacement>((_, _) => { Placements++; return Task.FromResult(Path.Combine(root.Path, "placed.epub")); });
            var roots = Proxy<ILibraryScanRootPersistence>((_, _) => Task.FromResult<LibraryRootData?>(root));
            var materializer = Proxy<IImportedEntityMaterializer>((_, args) => {
                Materializations++;
                var request = (ImportedEntityMaterializationRequest)args![2]!;
                return Task.FromResult(new ImportedEntityMaterializationResult([new(EntityId, EntityKind.Book)], [], request.PlacedMediaPaths, [], []));
            });
            var job = new JobRunSnapshot(Guid.NewGuid(), JobType.IntegrationTransfer, JobRunStatus.Running, 0, null, "{}",
                JobTargetKinds.IntegrationTransfer, State.OperationId.ToString(), "Publication", DateTimeOffset.UtcNow, null, null);
            var queue = Proxy<IJobQueueService>((method, _) => method switch {
                nameof(IJobQueueService.UpdateProgressAsync) => Task.CompletedTask,
                nameof(IJobQueueService.IsRunCancelledAsync) or nameof(IJobQueueService.HasPendingAsync) => Task.FromResult(false),
                nameof(IJobQueueService.EnqueueChildAsync) => Task.FromResult(job),
                _ => throw new NotSupportedException(method)
            });
            return new RemoteTransferProcessor(store, new(connections, plugins), gateway, new(gateway), bytes, verifier, placement, roots, materializer)
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
        private Task<FindTransferResult> Find() { Lookups++; return Task.FromResult(new FindTransferResult(accepted ? Snapshot : null)); }
        private Task<RemoteTransferSnapshot> Submit(SubmitTransferInput input) {
            Assert.Equal(IntegrationTransferPhase.SubmissionUncertain, State.Phase);
            Assert.Equal(State.OperationId, input.ClientOperationId);
            Submissions++; accepted = true;
            if (LoseSubmissionOnce) { LoseSubmissionOnce = false; throw new IntegrationInvocationException("Response lost"); }
            return Task.FromResult(Snapshot);
        }
        private Task<TransferRetentionResult> Renew(RenewTransferRetentionInput input) { Renewals++; return Task.FromResult(new TransferRetentionResult(input.JobId, input.RetainUntil)); }
        private Task<VerifiedIntegrationArtifact> Download(IntegrationArtifactTransferRequest request) {
            Assert.Equal("book.epub", request.Delivery.SuggestedFileName);
            Downloads++; return Task.FromResult(new VerifiedIntegrationArtifact(Artifact.Id, Path.Combine(root.Path, "staged.epub"), 100, Hash, "book.epub"));
        }
        private Task<TransferAcknowledgement> Acknowledge(AcknowledgeTransferInput input) {
            Assert.Equal(IntegrationTransferPhase.AwaitingAcknowledgement, State.Phase);
            Assert.Equal(State.ReceiptId, input.ReceiptId);
            Assert.Equal(EntityId, Assert.Single(Assert.Single(input.Artifacts).EntityIds));
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
