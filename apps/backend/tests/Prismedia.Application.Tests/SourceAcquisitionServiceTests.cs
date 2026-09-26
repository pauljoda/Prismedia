using System.Reflection;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Security;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class SourceAcquisitionServiceTests {
    [Fact]
    public async Task ReadOnlyObservationCreatesPendingIntentWithoutDispatchingRemotePreparation() {
        var fixture = new Fixture();
        var accepted = await fixture.AcquireAsync();
        Assert.Equal(IntegrationTransferMode.SourceRequest, accepted.Mode);
        Assert.Equal(IntegrationTransferPhase.PendingSubmission, accepted.Phase);
        Assert.Equal(1, fixture.Observations);
        Assert.Equal(1, fixture.Creates);
        Assert.Equal(fixture.Publication, fixture.Stored!.Plan.Source!.Publication);
        Assert.Equal(fixture.Offer, fixture.Stored.Plan.Source.Offer);
    }

    [Fact]
    public async Task OperationReplayPrecedesExpiredSelectionValidationAndNeverCreatesAgain() {
        var fixture = new Fixture();
        var accepted = await fixture.AcquireAsync();
        fixture.SelectionUnavailable = true;
        var replay = await fixture.AcquireAsync();
        Assert.Equal(accepted.Id, replay.Id);
        Assert.Equal(1, fixture.Observations);
        Assert.Equal(1, fixture.Creates);
        await Assert.ThrowsAsync<IntegrationTransferConflictException>(() =>
            fixture.AcquireAsync(fixture.Request with { OfferId = "replacement" }));
    }

    [Theory]
    [InlineData(IntegrationOperation.Resolve)]
    [InlineData(IntegrationOperation.RequestSource)]
    [InlineData(IntegrationOperation.ObserveSource)]
    public async Task EveryRequiredSourceOperationMustRemainEffective(IntegrationOperation missing) {
        var fixture = new Fixture(missing);
        await Assert.ThrowsAsync<ConnectionCapabilityUnavailableException>(fixture.AcquireAsync);
        Assert.Equal(0, fixture.Creates);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task ChangedIdentityInvalidMetadataOrUnsupportedFormatCannotCreateIntent(bool identity, bool metadata, bool format) {
        var fixture = new Fixture {
            ChangeSelection = identity,
            InvalidPublication = metadata,
            InvalidFormat = format
        };
        await Assert.ThrowsAsync<IntegrationInvocationException>(fixture.AcquireAsync);
        Assert.Equal(0, fixture.Creates);
    }

    private sealed class Fixture {
        private readonly SourceSelection selection = new("publication", "pinned-locator", EntityKind.Book);
        private readonly IntegrationConnection connection;
        private readonly SourceAcquisitionService service;
        internal LibraryRootData Root { get; } = new(Guid.NewGuid(), Path.GetTempPath(), "Books", true, false, false, false, false, true, false, false);
        internal CatalogPublication Publication { get; } = new("Publication", "Description", ["Creator"], new Dictionary<string, string>());
        internal CatalogOffer Offer { get; } = new("original", "Request", AcquisitionAccessKind.Request, "application/epub+zip");
        internal AcquireCatalogOfferRequest Request { get; }
        internal StoredIntegrationTransfer? Stored { get; private set; }
        internal bool SelectionUnavailable { get; set; }
        internal bool ChangeSelection { get; set; }
        internal bool InvalidPublication { get; set; }
        internal bool InvalidFormat { get; set; }
        internal int Observations { get; private set; }
        internal int Creates { get; private set; }

        internal Fixture(IntegrationOperation? missing = null) {
            var operations = new[] { IntegrationOperation.Resolve, IntegrationOperation.RequestSource, IntegrationOperation.ObserveSource }
                .Where(operation => operation != missing).ToArray();
            var supports = new IntegrationSupport[] { new(PluginCapability.AcquisitionSource, operations, [EntityKind.Book]) };
            connection = IntegrationConnection.Create("source", "Source", "https://source.test", true,
                [PluginCapability.AcquisitionSource], new Dictionary<string, string>());
            connection.RecordProbe(null, supports, null, DateTimeOffset.UtcNow, false);
            var manifest = new PluginManifest(2, [], "source", "Source", "1.0.0", "dotnet-process", "plugin.dll",
                new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, supports.Select(item => new PluginIntegrationCapability(item.Kind, item.Operations, item.EntityKinds)).ToArray(), []));
            Request = new(Guid.NewGuid(), "protected-selection", Offer.Id, Root.Id);
            var connections = Proxy<IIntegrationConnectionStore>((method, _) => method switch {
                nameof(IIntegrationConnectionStore.FindAsync) => Task.FromResult<StoredIntegrationConnection?>(new(connection, [])),
                nameof(IIntegrationConnectionStore.ReadSecretsAsync) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()),
                _ => throw new NotSupportedException(method)
            });
            var plugins = Proxy<IIntegrationPluginGateway>((method, _) => method == nameof(IIntegrationPluginGateway.FindAsync)
                ? Task.FromResult<PluginManifest?>(manifest) : throw new NotSupportedException(method));
            var access = new IntegrationConnectionAccess(connections, plugins);
            var tokens = Proxy<IDiscoveryTokenProtector>((_, _) =>
                SelectionUnavailable ? throw new ArgumentException("Selection expired") : selection);
            var gateway = Proxy<IIntegrationSourceAcquisitionGateway>((method, _) => method switch {
                nameof(IIntegrationSourceAcquisitionGateway.ObserveSourceAsync) => Observe(),
                nameof(IIntegrationSourceAcquisitionGateway.RequestSourceAsync) => throw new InvalidOperationException("API acceptance dispatched preparation"),
                _ => throw new NotSupportedException(method)
            });
            var store = Proxy<IIntegrationTransferStore>((method, args) => method switch {
                nameof(IIntegrationTransferStore.FindAsync) => Task.FromResult(Stored),
                nameof(IIntegrationTransferStore.CreateAsync) => (object)Create((IntegrationTransfer)args![0]!, (IntegrationTransferPlan)args[1]!),
                _ => throw new NotSupportedException(method)
            });
            var roots = Proxy<ILibraryScanRootPersistence>((_, _) => Task.FromResult<LibraryRootData?>(Root));
            var user = Proxy<ICurrentUserContext>((_, _) => ValueTask.FromResult<IReadOnlySet<Guid>?>(null));
            service = new(store, tokens, access, gateway, roots, user);
        }

        internal Task<IntegrationTransferResponse> AcquireAsync() => AcquireAsync(Request);
        internal Task<IntegrationTransferResponse> AcquireAsync(AcquireCatalogOfferRequest request) =>
            service.AcquireAsync(connection.State.Id, request, default);
        private Task<SourceAcquisitionObservation> Observe() {
            Observations++;
            return Task.FromResult(new SourceAcquisitionObservation(
                ChangeSelection ? selection with { ItemId = "replacement" } : selection,
                Offer.Id,
                InvalidPublication ? Publication with { Title = "" } : Publication,
                InvalidFormat ? Offer with { MediaType = "video/mp4" } : Offer,
                SourceAcquisitionState.NotObserved));
        }
        private Task<StoredIntegrationTransfer> Create(IntegrationTransfer transfer, IntegrationTransferPlan plan) {
            Creates++;
            Stored = new(transfer, plan, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            return Task.FromResult(Stored);
        }
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
