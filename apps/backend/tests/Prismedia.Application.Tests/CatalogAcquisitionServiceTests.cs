using System.Reflection;
using Prismedia.Application.Integrations;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Security;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class CatalogAcquisitionServiceTests {
    [Fact]
    public async Task AcceptedSourceMetadataAndAttributionStayWithTheOriginalIntent() {
        var fixture = new Fixture();
        fixture.Publication = fixture.Publication with { Attribution = new("https://catalog.test/source", "Original creator", "Original credit", "CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/", "Attribution", true) };
        var accepted = await fixture.AcquireAsync();
        Assert.Equal(fixture.Publication, accepted.SourcePublication);
        Assert.Equal(fixture.Publication, fixture.Stored!.Plan.Source!.Publication);
        fixture.Publication = fixture.Publication with { Attribution = fixture.Publication.Attribution! with { Creator = "Changed upstream" } };
        var replay = await fixture.AcquireAsync();
        Assert.Equal("Original creator", replay.SourcePublication!.Attribution!.Creator);
    }

    [Fact]
    public async Task ImageOfferUsesAnImageLibraryAndReplayDoesNotNeedTheSourceAgain() {
        var fixture = new Fixture();
        var accepted = await fixture.AcquireAsync();
        Assert.Equal(IntegrationTransferPhase.Transferring, accepted.Phase);
        Assert.Equal(EntityKind.Image, fixture.Stored!.Plan.EntityKind);
        fixture.SourceUnavailable = true;
        Assert.Equal(accepted.Id, (await fixture.AcquireAsync()).Id);
        Assert.Equal(1, fixture.Creates);
        await Assert.ThrowsAsync<IntegrationTransferConflictException>(() => fixture.AcquireAsync(fixture.Request with { OfferId = "another-offer" }));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    public async Task IncompatibleOrReadOnlyOrDisabledRootsCannotAcceptImages(bool enabled, bool scanImages, bool readOnly) {
        var fixture = new Fixture();
        fixture.Root = fixture.Root with { Enabled = enabled, ScanImages = scanImages, IsReadOnly = readOnly };
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.AcquireAsync());
        Assert.Null(fixture.Stored);
    }

    [Fact]
    public async Task UnavailableRootAndUnsupportedImageFormatCannotCreateIntent() {
        var fixture = new Fixture { DenyRoot = true };
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.AcquireAsync());
        fixture.DenyRoot = false;
        fixture.Delivery = fixture.Delivery with { SuggestedFileName = "image.svg" };
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.AcquireAsync());
        Assert.Null(fixture.Stored);
    }

    [Fact]
    public async Task DeclaredOversizedImageCannotCreateIntent() {
        var fixture = new Fixture();
        fixture.Delivery = fixture.Delivery with { ByteSize = IntegrationImportPolicy.For(EntityKind.Image).MaximumBytes + 1 };
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.AcquireAsync());
        Assert.Null(fixture.Stored);
    }

    private sealed class Fixture {
        internal LibraryRootData Root { get; set; } = new(Guid.NewGuid(), Path.GetTempPath(), "Images", true, false, false, true, false, false, false, false);
        internal bool DenyRoot { get; set; }
        internal bool SourceUnavailable { get; set; }
        internal int Creates { get; private set; }
        internal StoredIntegrationTransfer? Stored { get; private set; }
        internal HttpArtifactDelivery Delivery { get; set; } = new("https://catalog.test/image.jpg", new Dictionary<string, string>(), "image.jpg", 100);
        internal CatalogPublication Publication { get; set; } = new("Image", "Source description", ["Creator"], new Dictionary<string, string>());
        internal AcquireCatalogOfferRequest Request { get; }
        private readonly CatalogAcquisitionService service;
        private readonly IntegrationConnection connection;

        internal Fixture() {
            var supports = new IntegrationSupport[] { new(PluginCapability.AcquisitionSource, [IntegrationOperation.Resolve], [EntityKind.Image]) };
            connection = IntegrationConnection.Create("image-catalog", "Images", "https://catalog.test", true, [PluginCapability.AcquisitionSource], new Dictionary<string, string>());
            connection.RecordProbe(null, supports, null, DateTimeOffset.UtcNow, false);
            var manifest = new PluginManifest(2, [], "image-catalog", "Images", "1.0.0", "dotnet-process", "plugin.dll", new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, supports.Select(item => new PluginIntegrationCapability(item.Kind, item.Operations, item.EntityKinds)).ToArray(), []));
            Request = new(Guid.NewGuid(), "protected-selection", "original", Root.Id);
            var selection = new SourceSelection("image-one", "opaque-image-version", EntityKind.Image);
            var connections = Proxy<IIntegrationConnectionStore>((method, _) => method switch {
                nameof(IIntegrationConnectionStore.FindAsync) => Task.FromResult<StoredIntegrationConnection?>(new(connection, [])),
                nameof(IIntegrationConnectionStore.ReadSecretsAsync) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()),
                _ => throw new NotSupportedException(method)
            });
            var plugins = Proxy<IIntegrationPluginGateway>((_, _) => Task.FromResult<PluginManifest?>(manifest));
            var tokens = Proxy<IDiscoveryTokenProtector>((_, _) => SourceUnavailable ? throw new ArgumentException("Source selection expired") : selection);
            var gateway = Proxy<IIntegrationDiscoveryGateway>((_, _) => SourceUnavailable ? throw new IOException("Catalog offline") : Task.FromResult(
                new ResolvedSourceOffer(selection, Request.OfferId, Publication, new(Request.OfferId, "Original", AcquisitionAccessKind.Download, "image/jpeg", 100), Delivery)));
            var store = Proxy<IIntegrationTransferStore>((method, args) => method switch {
                nameof(IIntegrationTransferStore.FindAsync) => Task.FromResult(Stored),
                nameof(IIntegrationTransferStore.CreateAsync) => (object)Create((IntegrationTransfer)args![0]!, (IntegrationTransferPlan)args[1]!),
                _ => throw new NotSupportedException(method)
            });
            var roots = Proxy<ILibraryScanRootPersistence>((_, _) => Task.FromResult<LibraryRootData?>(Root));
            var user = Proxy<ICurrentUserContext>((_, _) => ValueTask.FromResult<IReadOnlySet<Guid>?>(DenyRoot ? new HashSet<Guid>() : null));
            service = new(store, tokens, new(new(connections, plugins), gateway, tokens), roots, user);
        }
        internal Task<IntegrationTransferResponse> AcquireAsync(AcquireCatalogOfferRequest? request = null) => service.AcquireAsync(connection.State.Id, request ?? Request, default);
        private Task<StoredIntegrationTransfer> Create(IntegrationTransfer transfer, IntegrationTransferPlan plan) {
            Creates++; Stored = new(transfer, plan, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow); return Task.FromResult(Stored);
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
