using System.Reflection;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class SourceTransferProcessorTests {
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

    private sealed class Fixture : IIntegrationTransferStore, IIntegrationArtifactTransfer, IIntegrationPublicationVerifier,
        IIntegrationImportPlacement, IImportedEntityMaterializer {
        private static readonly string Hash = new('a', 64);
        internal Guid EntityId { get; } = Guid.NewGuid();
        internal IntegrationTransferState State { get; private set; }
        internal bool FailCompletionSaveOnce { get; set; }
        internal bool MissingStaging { get; set; }
        internal int LocalReads { get; private set; }
        internal int Materializations { get; private set; }
        internal int Placements { get; private set; }
        internal string? Error { get; private set; }
        private readonly LibraryRootData root = new(Guid.NewGuid(), Path.GetTempPath(), "Library", true, false, false, false, false, true, false, false);
        private readonly IntegrationTransferPlan plan;
        internal Fixture() {
            var transfer = IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), Guid.NewGuid());
            transfer.AcceptSourceArtifact(new("artifact", "publication", "book.epub", "application/epub+zip", 100, Hash, IntegrationArtifactRole.Content));
            State = transfer.State;
            plan = new("Publication", EntityKind.Book, root.Id, root.Path, "owner", Hash, new(new("publication", "unreachable-source", EntityKind.Book), "artifact"));
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
            var processor = new SourceTransferProcessor(this, null!, null!, this, this, this, roots, this);
            return processor.ProcessAsync(State.OperationId, new(Job, queue), default);
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
        public Task<ImportedEntityMaterializationResult> MaterializeAsync(EntityKind kind, JobContext context, ImportedEntityMaterializationRequest request, CancellationToken cancellationToken) {
            Materializations++;
            return Task.FromResult(new ImportedEntityMaterializationResult([new(EntityId, EntityKind.Book)], [], request.PlacedMediaPaths, [], []));
        }
        public Task<VerifiedIntegrationArtifact> TransferAsync(IntegrationArtifactTransferRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("Recovery attempted a remote download");
        public Task<IReadOnlyList<StoredIntegrationTransfer>> ListAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredIntegrationTransfer> CreateAsync(IntegrationTransfer transfer, IntegrationTransferPlan intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnqueueRetryAsync(Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    public class Boundary : DispatchProxy {
        public Func<string, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args);
    }
}
