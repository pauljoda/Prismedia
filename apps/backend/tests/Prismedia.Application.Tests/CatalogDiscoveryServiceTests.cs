using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class CatalogDiscoveryServiceTests {
    [Fact]
    public async Task DisabledOrChangedPluginCannotReadCredentialsOrInvokeDiscovery() {
        var fixture = new Fixture();
        fixture.PluginAvailable = false;
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.Service.BrowseAsync(fixture.Connection.State.Id, new(EntityKind.Book), default));
        Assert.Equal(0, fixture.SecretReads);
        Assert.Equal(0, fixture.DiscoveryCalls);
        fixture.PluginAvailable = true;
        fixture.Manifest = fixture.Manifest with { Integration = new(1, [], []) };
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.BrowseAsync(fixture.Connection.State.Id, new(EntityKind.Book), default));
        Assert.Equal(0, fixture.SecretReads);
    }

    [Fact]
    public async Task DiscoveryReturnsProtectedSelectionsAndPreservesAcquisitionDistinctions() {
        var fixture = new Fixture();
        var page = await fixture.Service.BrowseAsync(fixture.Connection.State.Id, new(EntityKind.Book), default);
        var item = Assert.Single(page.Items);
        Assert.Equal(Fixture.ProtectedSelection, item.SelectionToken);
        Assert.Equal(AcquisitionAccessKind.Borrow, item.Offers[0].Access);
        Assert.Equal(1, fixture.SecretReads);
    }

    [Fact]
    public async Task OversizedOrMismatchedPagesAreRejected() {
        var fixture = new Fixture();
        fixture.Page = fixture.Page with { Items = [fixture.Item, fixture.Item] };
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.Service.BrowseAsync(fixture.Connection.State.Id, new(EntityKind.Book, Limit: 1), default));
        fixture.Page = fixture.Page with { Items = [fixture.Item with { Selection = fixture.Item.Selection with { EntityKind = EntityKind.Movie } }] };
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.Service.BrowseAsync(fixture.Connection.State.Id, new(EntityKind.Book), default));
    }

    [Theory]
    [InlineData(AcquisitionAccessKind.Borrow)]
    [InlineData(AcquisitionAccessKind.Purchase)]
    [InlineData(AcquisitionAccessKind.Sample)]
    [InlineData(AcquisitionAccessKind.External)]
    public async Task NonFullContentOffersCannotBecomeExecutableAcquisitions(AcquisitionAccessKind access) {
        var fixture = new Fixture();
        fixture.Resolved = fixture.Resolved with { Offer = fixture.Resolved.Offer with { Access = access } };
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.Service.ResolveAsync(fixture.Connection.State.Id, Fixture.ProtectedSelection, Fixture.OfferId, default));
    }

    [Fact]
    public async Task ResolveRejectsSubstitutionOfTheSelectedPublication() {
        var fixture = new Fixture();
        fixture.Resolved = fixture.Resolved with { Selection = fixture.Item.Selection with { ItemId = "another-publication" } };
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.Service.ResolveAsync(fixture.Connection.State.Id, Fixture.ProtectedSelection, Fixture.OfferId, default));
    }

    private sealed class Fixture : IIntegrationConnectionStore, IIntegrationPluginGateway, IIntegrationDiscoveryGateway, IDiscoveryTokenProtector {
        internal const string ProtectedSelection = "protected-selection";
        internal const string OfferId = "offer-id";
        private const string PluginId = "test-catalog";
        public IntegrationConnection Connection { get; }
        public PluginManifest Manifest { get; set; }
        public bool PluginAvailable { get; set; } = true;
        public int SecretReads { get; private set; }
        public int DiscoveryCalls { get; private set; }
        public CatalogItem Item { get; }
        public CatalogPage Page { get; set; }
        public ResolvedSourceOffer Resolved { get; set; }
        public CatalogDiscoveryService Service { get; }

        public Fixture() {
            var supports = new IntegrationSupport[] {
                new(PluginCapability.CatalogDiscovery, [IntegrationOperation.Browse, IntegrationOperation.Search], [EntityKind.Book]),
                new(PluginCapability.AcquisitionSource, [IntegrationOperation.Resolve], [EntityKind.Book])
            };
            Connection = IntegrationConnection.Create(PluginId, "Catalog", "http://catalog.test/", true, supports.Select(item => item.Kind).ToArray(), new Dictionary<string, string>());
            Connection.RecordProbe("installation", supports, null, DateTimeOffset.UtcNow);
            Manifest = new(2, [], PluginId, "Catalog", "1.0.0", "dotnet-process", "plugin.dll", new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, supports.Select(item => new PluginIntegrationCapability(item.Kind, item.Operations, item.EntityKinds)).ToArray(), []));
            Item = new(new("publication", "http://catalog.test/catalog", EntityKind.Book), false, new("Book", null, [], new Dictionary<string, string>()), [new(OfferId, "Borrow", AcquisitionAccessKind.Borrow)]);
            Page = new("Catalog", [Item]);
            Resolved = new(Item.Selection, OfferId, Item.Publication, new(OfferId, "Download", AcquisitionAccessKind.Download), new("http://catalog.test/file.epub", new Dictionary<string, string>(), "book.epub"));
            Service = new(new(this, this), this, this);
        }
        public Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<StoredIntegrationConnection?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<StoredIntegrationConnection?>(id == Connection.State.Id ? new(Connection, []) : null);
        public Task SaveAsync(IntegrationConnection connection, long? expectedRevision, IReadOnlyDictionary<string, string?> secretChanges, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<string, string>> ReadSecretsAsync(Guid id, CancellationToken cancellationToken) { SecretReads++; return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()); }
        public Task DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PluginManifest?> FindAsync(string pluginId, CancellationToken cancellationToken) => Task.FromResult(PluginAvailable ? Manifest : null);
        public Task<ConnectionProbeResult> ProbeAsync(string pluginId, IntegrationConnectionContext connection, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<CatalogPage> DiscoverAsync(string pluginId, IntegrationOperation operation, IntegrationConnectionContext connection, IntegrationDiscoveryInput input, CancellationToken cancellationToken) { DiscoveryCalls++; return Task.FromResult(Page); }
        public Task<ResolvedSourceOffer> ResolveAsync(string pluginId, IntegrationConnectionContext connection, ResolveSourceOfferInput input, CancellationToken cancellationToken) => Task.FromResult(Resolved);
        public string ProtectSelection(Guid connectionId, SourceSelection selection) => ProtectedSelection;
        public SourceSelection ReadSelection(Guid connectionId, string token) => Item.Selection;
        public string ProtectCursor(Guid connectionId, BrowseConnectionRequest scope, string cursor) => "protected-cursor";
        public string ReadCursor(Guid connectionId, BrowseConnectionRequest scope, string token) => "source-cursor";
    }
}
