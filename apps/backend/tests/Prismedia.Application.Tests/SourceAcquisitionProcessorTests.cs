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

public sealed class SourceAcquisitionProcessorTests {
    [Fact]
    public async Task LostRequestResponseIsRecoveredByObservationWithoutAnotherDispatch() {
        var fixture = new Fixture { LoseRequestResponseOnce = true, StateAfterLostResponse = SourceAcquisitionState.Queued };
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.SubmissionUncertain, fixture.State.Phase);
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.AwaitingRemote, fixture.State.Phase);
        Assert.Equal(1, fixture.Requests);
        fixture.SourceState = SourceAcquisitionState.Ready;
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(1, fixture.Requests);
        Assert.Equal(1, fixture.Downloads);
        Assert.Equal(1, fixture.Imports);
    }

    [Fact]
    public async Task AmbiguousRequestIsRepeatedWithTheSameOperationOnlyWhileNotObserved() {
        var fixture = new Fixture { LoseRequestResponseOnce = true };
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.Equal(2, fixture.Requests);
        Assert.All(fixture.RequestOperationIds, id => Assert.Equal(fixture.State.OperationId, id));
        Assert.Equal(IntegrationTransferPhase.AwaitingRemote, fixture.State.Phase);
    }

    [Fact]
    public async Task FailedPreparationStaysReviewableUntilTheSourceChanges() {
        var fixture = new Fixture { SourceState = SourceAcquisitionState.Failed };
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.NeedsReview, fixture.State.Phase);
        Assert.Equal("Preparation failed", fixture.State.SourceProblem);
        await fixture.RunAsync();
        Assert.Equal(0, fixture.Requests);
        Assert.Equal(0, fixture.Downloads);
    }

    [Fact]
    public async Task CancellationAfterReadyFencePreventsAStaleWorkerFromResolvingOrImporting() {
        var fixture = new Fixture { SourceState = SourceAcquisitionState.Ready, CancelAfterReadySave = true };
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Cancelled, fixture.State.Phase);
        Assert.Equal(0, fixture.Resolves);
        Assert.Equal(0, fixture.Downloads);
        Assert.Equal(0, fixture.Imports);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task ChangedSelectionOfferOrPublicationCannotAdvanceAcceptedScope(bool selection, bool offer, bool publication) {
        var fixture = new Fixture { SourceState = SourceAcquisitionState.Queued };
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        fixture.SourceState = SourceAcquisitionState.Ready;
        fixture.ChangeSelection = selection;
        fixture.ChangeOfferFormat = offer;
        fixture.ChangePublication = publication;
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.AwaitingRemote, fixture.State.Phase);
        Assert.Equal(0, fixture.Downloads);
        Assert.Equal(0, fixture.Imports);
    }

    [Fact]
    public async Task CompletedImportNeverResolvesDownloadsOrImportsAgain() {
        var fixture = new Fixture { SourceState = SourceAcquisitionState.Ready };
        await fixture.RunAsync();
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(1, fixture.Resolves);
        Assert.Equal(1, fixture.Downloads);
        Assert.Equal(1, fixture.Imports);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ChangedPublicationOrFormatOnFinalResolveIsRejectedBeforeByteTransfer(bool publication, bool format) {
        var fixture = new Fixture {
            SourceState = SourceAcquisitionState.Ready,
            ChangeResolvedPublication = publication,
            ChangeResolvedFormat = format
        };
        await Assert.ThrowsAsync<JobRetryLaterException>(fixture.RunAsync);
        Assert.Equal(IntegrationTransferPhase.Transferring, fixture.State.Phase);
        Assert.Equal(1, fixture.Resolves);
        Assert.Equal(0, fixture.Downloads);
        Assert.Equal(0, fixture.Imports);
    }

    private sealed class Fixture : IIntegrationTransferStore, IIntegrationSourceAcquisitionGateway,
        IIntegrationArtifactTransfer, IIntegrationMediaVerifier, IIntegrationImportPlacement, IImportedEntityMaterializer {
        private static readonly string Hash = new('a', 64);
        private readonly SourceSelection selection = new("publication", "pinned-locator", EntityKind.Book);
        private readonly CatalogPublication publication = new("Publication", "Description", ["Creator"],
            new Dictionary<string, string> { ["source"] = "publication" });
        private readonly CatalogOffer requestOffer = new("original", "Request", AcquisitionAccessKind.Request, "application/epub+zip");
        private readonly LibraryRootData root = new(Guid.NewGuid(), Path.GetTempPath(), "Books", true, false, false, false, false, true, false, false);
        private readonly IntegrationConnection connection;
        private readonly PluginManifest manifest;
        private readonly IntegrationTransferPlan plan;
        internal IntegrationTransferState State { get; private set; }
        internal SourceAcquisitionState SourceState { get; set; } = SourceAcquisitionState.NotObserved;
        internal SourceAcquisitionState? StateAfterLostResponse { get; set; }
        internal bool LoseRequestResponseOnce { get; set; }
        internal bool CancelAfterReadySave { get; set; }
        internal bool ChangeSelection { get; set; }
        internal bool ChangeOfferFormat { get; set; }
        internal bool ChangePublication { get; set; }
        internal bool ChangeResolvedPublication { get; set; }
        internal bool ChangeResolvedFormat { get; set; }
        internal int Requests { get; private set; }
        internal int Resolves { get; private set; }
        internal int Downloads { get; private set; }
        internal int Imports { get; private set; }
        internal List<Guid> RequestOperationIds { get; } = [];

        internal Fixture() {
            var operations = new[] { IntegrationOperation.Resolve, IntegrationOperation.RequestSource, IntegrationOperation.ObserveSource };
            var supports = new IntegrationSupport[] { new(PluginCapability.AcquisitionSource, operations, [EntityKind.Book]) };
            connection = IntegrationConnection.Create("source", "Source", "https://source.test", true,
                [PluginCapability.AcquisitionSource], new Dictionary<string, string>());
            connection.RecordProbe(null, supports, null, DateTimeOffset.UtcNow, false);
            manifest = new(2, [], "source", "Source", "1.0.0", "dotnet-process", "plugin.dll",
                new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, supports.Select(item => new PluginIntegrationCapability(item.Kind, item.Operations, item.EntityKinds)).ToArray(), [], ["https://files.test"]));
            var transfer = IntegrationTransfer.CreateSourceRequest(Guid.NewGuid(), connection.State.Id);
            State = transfer.State;
            plan = new("Publication", EntityKind.Book, root.Id, root.Path, "owner", Hash,
                new(selection, requestOffer.Id, publication, requestOffer));
        }

        internal Task RunAsync() {
            var access = CreateAccess();
            var discoveryGateway = Proxy<IIntegrationDiscoveryGateway>((method, _) => method == nameof(IIntegrationDiscoveryGateway.ResolveAsync)
                ? Resolve() : throw new NotSupportedException(method));
            var roots = Proxy<ILibraryScanRootPersistence>((_, _) => Task.FromResult<LibraryRootData?>(root));
            var sourceTransfers = new SourceTransferProcessor(this, new(access, discoveryGateway, null!), access,
                this, this, this, roots, this);
            var processor = new SourceAcquisitionProcessor(this, access, this, sourceTransfers);
            var queue = Proxy<IJobQueueService>((method, _) => method switch {
                nameof(IJobQueueService.UpdateProgressAsync) => Task.CompletedTask,
                nameof(IJobQueueService.IsRunCancelledAsync) or nameof(IJobQueueService.HasPendingAsync) => Task.FromResult(false),
                nameof(IJobQueueService.EnqueueChildAsync) => Task.FromResult(Job),
                _ => throw new NotSupportedException(method)
            });
            return processor.ProcessAsync(State.OperationId, new(Job, queue), default);
        }

        private IntegrationConnectionAccess CreateAccess() {
            var connections = Proxy<IIntegrationConnectionStore>((method, _) => method switch {
                nameof(IIntegrationConnectionStore.FindAsync) => Task.FromResult<StoredIntegrationConnection?>(new(connection, [])),
                nameof(IIntegrationConnectionStore.ReadSecretsAsync) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()),
                _ => throw new NotSupportedException(method)
            });
            var plugins = Proxy<IIntegrationPluginGateway>((method, _) => method == nameof(IIntegrationPluginGateway.FindAsync)
                ? Task.FromResult<PluginManifest?>(manifest) : throw new NotSupportedException(method));
            return new(connections, plugins);
        }

        private JobRunSnapshot Job => new(Guid.NewGuid(), JobType.IntegrationTransfer, JobRunStatus.Running, 0, null, "{}",
            JobTargetKinds.IntegrationTransfer, State.OperationId.ToString(), "Publication", DateTimeOffset.UtcNow, null, null);
        private SourceAcquisitionObservation Observation(SourceAcquisitionState state) => new(
            ChangeSelection ? selection with { ItemId = "replacement" } : selection,
            requestOffer.Id,
            ChangePublication ? publication with { Title = "Replacement" } : publication,
            requestOffer with {
                Label = state == SourceAcquisitionState.Ready ? "Download" : requestOffer.Label,
                Access = state == SourceAcquisitionState.Ready ? AcquisitionAccessKind.Download : AcquisitionAccessKind.Request,
                MediaType = ChangeOfferFormat ? "application/pdf" : requestOffer.MediaType
            },
            state, state == SourceAcquisitionState.Downloading ? 0.5 : null,
            DateTimeOffset.UtcNow.AddSeconds(20), state == SourceAcquisitionState.Failed ? "Preparation failed" : null);
        private Task<ResolvedSourceOffer> Resolve() {
            Resolves++;
            return Task.FromResult(new ResolvedSourceOffer(selection, requestOffer.Id,
                ChangeResolvedPublication ? publication with { Title = "Replacement" } : publication,
                requestOffer with {
                    Label = "Download", Access = AcquisitionAccessKind.Download,
                    MediaType = ChangeResolvedFormat ? "application/pdf" : requestOffer.MediaType
                },
                new("https://files.test/book.epub", new Dictionary<string, string>(), "book.epub", 100, Hash)));
        }

        public Task<SourceAcquisitionObservation> ObserveSourceAsync(string pluginId, IntegrationConnectionContext context,
            ObserveSourceInput input, CancellationToken cancellationToken) => Task.FromResult(Observation(SourceState));
        public Task<SourceAcquisitionObservation> RequestSourceAsync(string pluginId, IntegrationConnectionContext context,
            RequestSourceInput input, CancellationToken cancellationToken) {
            Requests++;
            RequestOperationIds.Add(input.OperationId);
            Assert.Equal(selection, input.Selection);
            Assert.Equal(requestOffer.Id, input.OfferId);
            if (LoseRequestResponseOnce) {
                LoseRequestResponseOnce = false;
                if (StateAfterLostResponse is { } next) SourceState = next;
                throw new IntegrationInvocationException("Response lost");
            }
            SourceState = SourceAcquisitionState.Queued;
            return Task.FromResult(Observation(SourceState));
        }
        public Task<StoredIntegrationTransfer?> FindAsync(Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult<StoredIntegrationTransfer?>(new(new(State), plan, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        public Task SaveAsync(IntegrationTransfer transfer, long expectedRevision, string? error, CancellationToken cancellationToken) {
            Assert.Equal(State.Revision, expectedRevision);
            State = transfer.State;
            if (CancelAfterReadySave && State.Phase == IntegrationTransferPhase.Transferring) {
                CancelAfterReadySave = false;
                var cancelled = new IntegrationTransfer(State);
                cancelled.CancelSourceDownload();
                State = cancelled.State;
            }
            return Task.CompletedTask;
        }
        public Task RecordErrorAsync(Guid operationId, long expectedRevision, string error, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<VerifiedIntegrationArtifact> TransferAsync(IntegrationArtifactTransferRequest request, CancellationToken cancellationToken) {
            Downloads++;
            return Task.FromResult(new VerifiedIntegrationArtifact(request.ArtifactId, Path.Combine(root.Path, "staged.epub"), 100, Hash, "book.epub"));
        }
        public Task<VerifiedIntegrationArtifact?> ReadVerifiedAsync(Guid operationId, string artifactId, string fileName,
            long sizeBytes, string sha256, CancellationToken cancellationToken) =>
            Task.FromResult<VerifiedIntegrationArtifact?>(new(artifactId, Path.Combine(root.Path, "staged.epub"), sizeBytes, sha256, fileName));
        public Task VerifyAsync(VerifiedIntegrationArtifact artifact, EntityKind kind, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> PlaceAsync(Guid operationId, IntegrationTransferPlan acceptedPlan, LibraryRootData acceptedRoot,
            VerifiedIntegrationArtifact artifact, CancellationToken cancellationToken) => Task.FromResult(Path.Combine(root.Path, "placed.epub"));
        public Task<VerifiedIntegrationArtifact?> ReadPlacedAsync(Guid operationId, IntegrationTransferPlan acceptedPlan,
            LibraryRootData acceptedRoot, IntegrationArtifact artifact, CancellationToken cancellationToken) =>
            Task.FromResult<VerifiedIntegrationArtifact?>(null);
        public Task<ImportedEntityMaterializationResult> MaterializeAsync(EntityKind kind, JobContext context,
            ImportedEntityMaterializationRequest request, CancellationToken cancellationToken) {
            Imports++;
            return Task.FromResult(new ImportedEntityMaterializationResult([new(Guid.NewGuid(), kind)], [], request.PlacedMediaPaths, [], []));
        }
        public Task<IReadOnlyList<StoredIntegrationTransfer>> ListAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredIntegrationTransfer> CreateAsync(IntegrationTransfer transfer, IntegrationTransferPlan acceptedPlan, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnqueueRetryAsync(Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        private static T Proxy<T>(Func<string, object?[]?, object?> call) where T : class {
            var result = DispatchProxy.Create<T, Boundary>();
            ((Boundary)(object)result).Call = call;
            return result;
        }
    }

    public class Boundary : DispatchProxy {
        public Func<string, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args);
    }
}
