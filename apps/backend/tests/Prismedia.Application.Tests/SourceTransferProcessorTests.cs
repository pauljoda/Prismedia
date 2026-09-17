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

public sealed class SourceTransferProcessorTests {
    [Fact]
    public async Task ImageDownloadUsesTheImageByteBudgetAndRecordsExactImageOwnership() {
        var fixture = new Fixture(pendingDownload: true, kind: EntityKind.Image);
        await fixture.RunAsync();
        Assert.Equal(IntegrationMediaFormats.MaximumImageBytes, fixture.Download!.MaximumBytes);
        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(fixture.EntityId, Assert.Single(Assert.Single(fixture.State.Imports!).EntityIds));
    }
    [Fact]
    public async Task DeclaredForeignDownloadIsPassedToTransportWithoutHeaders() {
        var fixture = new Fixture(pendingDownload: true);
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal("https://files.test", fixture.Download!.AllowedOrigin);
        Assert.Empty(fixture.Download.Delivery.Headers);
    }

    [Fact]
    public async Task RemovingOriginDuringResolutionBlocksWorkerBeforeByteRetrieval() {
        var fixture = new Fixture(pendingDownload: true) { RemoveOriginAfterResolve = true };
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.RunAsync());
        Assert.Null(fixture.Download);
        Assert.Equal(0, fixture.Placements);
        Assert.Equal(IntegrationTransferPhase.Transferring, fixture.State.Phase);
    }

    [Fact]
    public async Task RestartAfterCommittedMaterializationUsesLocalBytesAndKeepsExactEntityReceipt() {
        var fixture = new Fixture { FailCompletionSaveOnce = true };
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.RunAsync());
        Assert.Equal(IntegrationTransferPhase.Importing, fixture.State.Phase);
        Assert.Empty(fixture.State.Imports!);
        await fixture.RunAsync();
        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(fixture.EntityId, Assert.Single(Assert.Single(fixture.State.Imports!).EntityIds));
        Assert.Equal(2, fixture.LocalReads);
        Assert.Equal(2, fixture.Materializations);
        Assert.Null(fixture.Error);
        await fixture.RunAsync();
        Assert.Equal(2, fixture.LocalReads);
    }

    [Fact]
    public async Task MissingVerifiedStagingDoesNotContactSourceOrPlaceFiles() {
        var fixture = new Fixture { MissingStaging = true };
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.RunAsync());
        Assert.Equal(IntegrationTransferPhase.Importing, fixture.State.Phase);
        Assert.Equal(0, fixture.Materializations);
        Assert.Equal(0, fixture.Placements);
        Assert.NotNull(fixture.Error);
    }

    [Fact]
    public async Task MissingStagingRecoversExactPlacedPublicationWithoutSourceDownload() {
        var fixture = new Fixture { MissingStaging = true, PlacedRecoveryAvailable = true };

        await fixture.RunAsync();

        Assert.Equal(IntegrationTransferPhase.Completed, fixture.State.Phase);
        Assert.Equal(0, fixture.Downloads);
        Assert.Equal(1, fixture.PlacedReads);
        Assert.Equal(1, fixture.Materializations);
    }

    private sealed class Fixture : IIntegrationTransferStore, IIntegrationArtifactTransfer, IIntegrationMediaVerifier,
        IIntegrationImportPlacement, IImportedEntityMaterializer {
        private static readonly string Hash = new('a', 64);
        internal Guid EntityId { get; } = Guid.NewGuid();
        internal IntegrationTransferState State { get; private set; }
        internal bool FailCompletionSaveOnce { get; set; }
        internal bool MissingStaging { get; set; }
        internal bool PlacedRecoveryAvailable { get; set; }
        internal int LocalReads { get; private set; }
        internal int PlacedReads { get; private set; }
        internal int Materializations { get; private set; }
        internal int Placements { get; private set; }
        internal string? Error { get; private set; }
        internal bool RemoveOriginAfterResolve { get; set; }
        internal IntegrationArtifactTransferRequest? Download { get; private set; }
        internal int Downloads { get; private set; }
        private readonly bool pendingDownload;
        private readonly EntityKind kind;
        private readonly string fileName;
        private readonly IntegrationConnection connection;
        private PluginManifest manifest;
        private readonly LibraryRootData root;
        private readonly IntegrationTransferPlan plan;
        internal Fixture(bool pendingDownload = false, EntityKind kind = EntityKind.Book) {
            this.pendingDownload = pendingDownload;
            this.kind = kind;
            fileName = kind == EntityKind.Image ? "image.jpg" : "book.epub";
            root = new(Guid.NewGuid(), Path.GetTempPath(), "Library", true, false, false, kind == EntityKind.Image, false, kind == EntityKind.Book, false, false);
            var supports = new IntegrationSupport[] { new(PluginCapability.AcquisitionSource, [IntegrationOperation.Resolve], [kind]) };
            connection = IntegrationConnection.Create("test-catalog", "Catalog", "https://catalog.test", true, [PluginCapability.AcquisitionSource], new Dictionary<string, string>());
            connection.RecordProbe(null, supports, null, DateTimeOffset.UtcNow, false);
            manifest = new(2, [], "test-catalog", "Catalog", "1.0.0", "dotnet-process", "plugin.dll", new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, supports.Select(item => new PluginIntegrationCapability(item.Kind, item.Operations, item.EntityKinds)).ToArray(), [], ["https://files.test"]));
            var transfer = IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), connection.State.Id);
            if (!pendingDownload) transfer.AcceptSourceArtifact(new("artifact", "publication", "book.epub", "application/epub+zip", 100, Hash, IntegrationArtifactRole.Content));
            State = transfer.State;
            plan = new("Publication", kind, root.Id, root.Path, "owner", Hash, new(new("publication", "unreachable-source", kind), "artifact"));
        }
        internal Task RunAsync() {
            var roots = DispatchProxy.Create<ILibraryScanRootPersistence, Boundary>();
            ((Boundary)(object)roots).Call = (method, _) => method == nameof(ILibraryScanRootPersistence.GetLibraryRootAsync) ? Task.FromResult<LibraryRootData?>(root) : throw new NotSupportedException(method);
            var queue = DispatchProxy.Create<IJobQueueService, Boundary>();
            ((Boundary)(object)queue).Call = (method, _) => method switch {
                nameof(IJobQueueService.UpdateProgressAsync) => Task.CompletedTask,
                nameof(IJobQueueService.IsRunCancelledAsync) or nameof(IJobQueueService.HasPendingAsync) => Task.FromResult(false),
                nameof(IJobQueueService.EnqueueChildAsync) => Task.FromResult(Job),
                _ => throw new NotSupportedException(method)
            };
            // Discovery/access are intentionally unavailable: recovery from Importing cannot depend on either.
            var access = pendingDownload ? CreateAccess() : null;
            var gateway = DispatchProxy.Create<IIntegrationDiscoveryGateway, Boundary>();
            ((Boundary)(object)gateway).Call = (_, _) => {
                if (RemoveOriginAfterResolve) manifest = manifest with { Integration = manifest.Integration! with { AnonymousArtifactOrigins = [] } };
                return Task.FromResult(new ResolvedSourceOffer(plan.Source!.Selection, plan.Source.OfferId,
                    new("Book", null, [], new Dictionary<string, string>()), new(plan.Source.OfferId, "Download", AcquisitionAccessKind.Download),
                    new("https://files.test/" + fileName, new Dictionary<string, string>(), fileName, 100, Hash)));
            };
            var discovery = pendingDownload ? new CatalogDiscoveryService(access!, gateway, null!) : null;
            var processor = new SourceTransferProcessor(this, discovery!, access!, this, this, this, roots, this);
            return processor.ProcessAsync(State.OperationId, new(Job, queue), default);
        }

        private IntegrationConnectionAccess CreateAccess() {
            var connections = DispatchProxy.Create<IIntegrationConnectionStore, Boundary>();
            ((Boundary)(object)connections).Call = (method, _) => method switch {
                nameof(IIntegrationConnectionStore.FindAsync) => Task.FromResult<StoredIntegrationConnection?>(new(connection, [])),
                nameof(IIntegrationConnectionStore.ReadSecretsAsync) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()),
                _ => throw new NotSupportedException(method)
            };
            var plugins = DispatchProxy.Create<IIntegrationPluginGateway, Boundary>();
            ((Boundary)(object)plugins).Call = (method, _) => method == nameof(IIntegrationPluginGateway.FindAsync)
                ? Task.FromResult<PluginManifest?>(manifest) : throw new NotSupportedException(method);
            return new(connections, plugins);
        }
        private JobRunSnapshot Job => new(Guid.NewGuid(), JobType.IntegrationTransfer, JobRunStatus.Running, 0, null, "{}",
            JobTargetKinds.IntegrationTransfer, State.OperationId.ToString(), "Publication", DateTimeOffset.UtcNow, null, null);
        public Task<StoredIntegrationTransfer?> FindAsync(Guid operationId, CancellationToken cancellationToken) =>
            Task.FromResult<StoredIntegrationTransfer?>(new(new(State), plan, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Error));
        public Task SaveAsync(IntegrationTransfer transfer, long expectedRevision, string? error, CancellationToken cancellationToken) {
            Assert.Equal(State.Revision, expectedRevision);
            if (FailCompletionSaveOnce) { FailCompletionSaveOnce = false; throw new IOException("Simulated lost commit"); }
            State = transfer.State; Error = error;
            return Task.CompletedTask;
        }
        public Task RecordErrorAsync(Guid operationId, long expectedRevision, string error, CancellationToken cancellationToken) { Error = error; return Task.CompletedTask; }
        public Task<VerifiedIntegrationArtifact?> ReadVerifiedAsync(Guid operationId, string artifactId, string fileName, long sizeBytes, string sha256, CancellationToken cancellationToken) {
            LocalReads++;
            return Task.FromResult<VerifiedIntegrationArtifact?>(MissingStaging ? null : new(artifactId, Path.Combine(root.Path, "staged.epub"), sizeBytes, sha256, fileName));
        }
        public Task VerifyAsync(VerifiedIntegrationArtifact artifact, EntityKind kind, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> PlaceAsync(Guid operationId, IntegrationTransferPlan plan, LibraryRootData root, VerifiedIntegrationArtifact artifact, CancellationToken cancellationToken) {
            Placements++; return Task.FromResult(Path.Combine(root.Path, "placed.epub"));
        }
        public Task<VerifiedIntegrationArtifact?> ReadPlacedAsync(Guid operationId, IntegrationTransferPlan acceptedPlan,
            LibraryRootData acceptedRoot, IntegrationArtifact artifact, CancellationToken cancellationToken) {
            PlacedReads++;
            Assert.Equal(plan, acceptedPlan);
            Assert.Equal(root, acceptedRoot);
            return Task.FromResult<VerifiedIntegrationArtifact?>(PlacedRecoveryAvailable
                ? new(artifact.Id, Path.Combine(root.Path, "placed.epub"), artifact.SizeBytes, artifact.Sha256, Path.GetFileName(artifact.RelativePath))
                : null);
        }
        public Task<ImportedEntityMaterializationResult> MaterializeAsync(EntityKind kind, JobContext context, ImportedEntityMaterializationRequest request, CancellationToken cancellationToken) {
            Materializations++;
            Assert.Equal(this.kind, kind);
            return Task.FromResult(new ImportedEntityMaterializationResult([new(EntityId, kind)], [], request.PlacedMediaPaths, [], []));
        }
        public Task<VerifiedIntegrationArtifact> TransferAsync(IntegrationArtifactTransferRequest request, CancellationToken cancellationToken) {
            if (!pendingDownload) throw new InvalidOperationException("Recovery attempted a remote download");
            Downloads++;
            Download = request;
            return Task.FromResult(new VerifiedIntegrationArtifact(request.ArtifactId, Path.Combine(root.Path, "staged.epub"), 100, Hash, request.Delivery.SuggestedFileName));
        }
        public Task<IReadOnlyList<StoredIntegrationTransfer>> ListAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredIntegrationTransfer> CreateAsync(IntegrationTransfer transfer, IntegrationTransferPlan intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnqueueRetryAsync(Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    public class Boundary : DispatchProxy {
        public Func<string, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args);
    }
}
