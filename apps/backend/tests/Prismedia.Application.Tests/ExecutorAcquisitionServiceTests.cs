using System.Reflection;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Security;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class ExecutorAcquisitionServiceTests {
    [Fact]
    public async Task AcceptancePersistsBeforeAnySubmitAndIdenticalReplaySurvivesExpiredInspection() {
        var fixture = new Fixture();
        var accepted = await fixture.Service.AcquireAsync(fixture.Connection.State.Id, fixture.Request, default);
        Assert.Equal(IntegrationTransferPhase.PendingSubmission, accepted.Phase);
        Assert.Equal(1, fixture.Creates);
        fixture.TokenUnavailable = true;
        var replay = await fixture.Service.AcquireAsync(fixture.Connection.State.Id, fixture.Request, default);
        Assert.Equal(accepted.Id, replay.Id);
        Assert.Equal(1, fixture.Creates);
        await Assert.ThrowsAsync<IntegrationTransferConflictException>(() => fixture.Service.AcquireAsync(fixture.Connection.State.Id,
            fixture.Request with { ItemId = "substituted-item" }, default));
    }
    [Fact]
    public async Task ChangedConnectionRequiresFreshInspectionBeforeCreatingIntent() {
        var fixture = new Fixture();
        fixture.Connection.RecordProbe(Fixture.InstanceId, Fixture.Supports, null, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.AcquireAsync(fixture.Connection.State.Id, fixture.Request, default));
        Assert.Equal(0, fixture.Creates);
    }
    [Fact]
    public async Task InaccessibleDestinationCannotAcquireOrPersistWork() {
        var fixture = new Fixture { DenyRoot = true };
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.AcquireAsync(fixture.Connection.State.Id, fixture.Request, default));
        Assert.Equal(0, fixture.Creates);
    }
    [Fact]
    public async Task InstallationWithoutReceiptSupportCannotInspectForExecutableAcquisition() {
        var fixture = new Fixture();
        fixture.Manifest = fixture.Manifest with { Integration = new(1,
            fixture.Manifest.Integration!.Capabilities.Select(capability => capability with {
                Operations = capability.Operations.Where(operation => operation != IntegrationOperation.Acknowledge).ToArray()
            }).ToArray(), []) };
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.InspectAsync(fixture.Connection.State.Id,
            new("https://source.test/publication", EntityKind.Book), default));
        Assert.Equal(0, fixture.Creates);
    }
    [Fact]
    public async Task ImageAcceptanceUsesAnImageLibraryAndPersistsABoundedIntent() {
        var fixture = new Fixture(EntityKind.Image);
        var result = await fixture.Service.AcquireAsync(fixture.Connection.State.Id, fixture.Request, default);
        Assert.Equal(IntegrationTransferPhase.PendingSubmission, result.Phase);
        Assert.Equal(1, fixture.Creates);
        Assert.Equal(64L * 1024 * 1024, fixture.AcceptedPlan!.Executor!.MaximumBytes);
    }
    [Fact]
    public async Task ImageCannotBeSentToABookOnlyLibrary() {
        var fixture = new Fixture(EntityKind.Image, imageLibrary: false);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.AcquireAsync(fixture.Connection.State.Id, fixture.Request, default));
        Assert.Equal(0, fixture.Creates);
    }
    [Fact]
    public async Task GalleryAcceptanceRequiresRecursiveImagesAndKeepsOneLogicalSelection() {
        var fixture = new Fixture(EntityKind.Gallery);
        await fixture.Service.AcquireAsync(fixture.Connection.State.Id, fixture.Request, default);
        Assert.Equal(1, fixture.AcceptedPlan!.Executor!.MaximumItems);
        Assert.Equal(IntegrationImportPolicy.PublicationByteLimit, fixture.AcceptedPlan.Executor.MaximumBytes);
        var nonrecursive = new Fixture(EntityKind.Gallery, recursive: false);
        await Assert.ThrowsAsync<ArgumentException>(() => nonrecursive.Service.AcquireAsync(nonrecursive.Connection.State.Id, nonrecursive.Request, default));
        Assert.Equal(0, nonrecursive.Creates);
    }
    private sealed class Fixture {
        internal const string InstanceId = "executor-installation";
        private const string PluginId = "executor-test";
        internal static readonly IntegrationSupport[] Supports = [
            new(PluginCapability.CatalogDiscovery, [IntegrationOperation.Inspect], [EntityKind.Book, EntityKind.Image, EntityKind.Gallery]),
            new(PluginCapability.TransferExecutor, [IntegrationOperation.Submit, IntegrationOperation.FindSubmission, IntegrationOperation.GetJob,
                IntegrationOperation.ListArtifacts, IntegrationOperation.AuthorizeArtifact, IntegrationOperation.RenewRetention, IntegrationOperation.Acknowledge, IntegrationOperation.Cancel, IntegrationOperation.CancelSubmission], [EntityKind.Book, EntityKind.Image, EntityKind.Gallery])
        ];
        internal IntegrationConnection Connection { get; }
        internal PluginManifest Manifest { get; set; }
        internal AcquireExecutorItemRequest Request { get; }
        internal ExecutorAcquisitionService Service { get; }
        internal bool TokenUnavailable { get; set; }
        internal bool DenyRoot { get; set; }
        internal int Creates { get; private set; }
        private StoredIntegrationTransfer? stored;
        internal IntegrationTransferPlan? AcceptedPlan => stored?.Plan;
        internal Fixture(EntityKind kind = EntityKind.Book, bool imageLibrary = true, bool recursive = true) {
            Connection = IntegrationConnection.Create(PluginId, "Executor", "http://executor.test/", true,
                Supports.Select(item => item.Kind).ToArray(), new Dictionary<string, string>());
            Connection.RecordProbe(InstanceId, Supports, null, DateTimeOffset.UtcNow);
            Manifest = new(2, [], PluginId, "Executor", "1.0.0", "dotnet-process", "plugin.dll", new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, Supports.Select(item => new PluginIntegrationCapability(item.Kind, item.Operations, item.EntityKinds)).ToArray(), []));
            var root = new LibraryRootData(Guid.NewGuid(), Path.GetTempPath(), "Library", true, recursive, false, kind is EntityKind.Image or EntityKind.Gallery && imageLibrary, false, kind is not (EntityKind.Image or EntityKind.Gallery) || !imageLibrary, false, false);
            Request = new(Guid.NewGuid(), "protected-selection", "publication", root.Id);
            var selection = new AcceptedExecutorSelection(InstanceId, Connection.State.Revision, kind,
                new("selection", "revision", DateTimeOffset.UtcNow.AddMinutes(5), "https://source.test/publication", [new(Request.ItemId, "Publication", kind)], false, []));
            var connections = Proxy<IIntegrationConnectionStore>((method, _) => method switch {
                nameof(IIntegrationConnectionStore.FindAsync) => Task.FromResult<StoredIntegrationConnection?>(new(Connection, [])),
                nameof(IIntegrationConnectionStore.ReadSecretsAsync) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()),
                _ => throw new NotSupportedException(method)
            });
            var plugins = Proxy<IIntegrationPluginGateway>((_, _) => Task.FromResult<PluginManifest?>(Manifest));
            var selections = Proxy<IExecutorSelectionProtector>((method, _) => method == nameof(IExecutorSelectionProtector.Read)
                ? TokenUnavailable ? throw new ArgumentException("Selection expired") : selection : throw new NotSupportedException(method));
            var store = Proxy<IIntegrationTransferStore>((method, args) => method switch {
                nameof(IIntegrationTransferStore.FindAsync) => Task.FromResult(stored),
                nameof(IIntegrationTransferStore.CreateAsync) => (object)Create((IntegrationTransfer)args![0]!, (IntegrationTransferPlan)args[1]!),
                _ => throw new NotSupportedException(method)
            });
            var roots = Proxy<ILibraryScanRootPersistence>((_, _) => Task.FromResult<LibraryRootData?>(root));
            var user = Proxy<ICurrentUserContext>((_, _) => ValueTask.FromResult<IReadOnlySet<Guid>?>(DenyRoot ? new HashSet<Guid>() : null));
            // No gateway methods are allowed: acceptance must only persist the intent, never submit it.
            var gateway = Proxy<IIntegrationTransferGateway>((method, _) => throw new InvalidOperationException("Unexpected remote effect: " + method));
            var queue = Proxy<IJobQueueService>((method, _) => throw new NotSupportedException(method));
            Service = new(new(connections, plugins), gateway, selections, store, roots, user, new(store, queue));
        }
        private Task<StoredIntegrationTransfer> Create(IntegrationTransfer transfer, IntegrationTransferPlan plan) {
            Creates++; stored = new(transfer, plan, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow); return Task.FromResult(stored);
        }
    }
    private static T Proxy<T>(Func<string, object?[]?, object?> call) where T : class {
        var result = DispatchProxy.Create<T, Boundary>(); ((Boundary)(object)result).Call = call; return result;
    }
    public class Boundary : DispatchProxy {
        public Func<string, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args);
    }
}
