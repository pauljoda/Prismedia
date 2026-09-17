using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using System.Text.Json;

namespace Prismedia.Application.Tests;

public sealed class ManagedLibraryServiceTests {
    private const string FixtureProvider = "fixture-catalog";
    private const string CurrentCredentialKey = "current-token";
    private const string RetiredCredentialKey = "retired-token";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProbeAndLibraryReadsRequestAndPassOnlyCurrentlyDeclaredCredentials(bool probe) {
        var fixture = new Fixture();
        fixture.Manifest = fixture.Manifest with { Auth = [new(CurrentCredentialKey, "Token", true, null)] };
        fixture.Secrets = new Dictionary<string, string> { [CurrentCredentialKey] = "current-secret", [RetiredCredentialKey] = "retired-secret" };
        if (probe) await new ConnectionService(fixture, fixture).ProbeAsync(fixture.Connection.State.Id, default);
        else await fixture.Service.SearchAsync(fixture.Connection.State.Id, new(EntityKind.Movie), default);
        Assert.Equal([CurrentCredentialKey], fixture.RequestedSecretKeys);
        Assert.Equal([CurrentCredentialKey], fixture.LastAuth!.Keys);
        Assert.Equal("current-secret", fixture.LastAuth[CurrentCredentialKey]);
    }
    [Fact]
    public void ComicEvidencePreservesExactLabelsWithoutBorrowingTelevisionNumbering() {
        var item = new ManagedLibraryItem("run", EntityKind.ComicSeries, "Comics", 2026,
            new Dictionary<string, string> { [FixtureProvider] = "fixture-run" }, false, null, 1);
        var input = new ManagedItemInput(item.EntityKind, item.RemoteId, item.ExternalIds);
        var snapshot = new ManagedItemSnapshot(item, "/comics/run", [new("file", "/comics/run/issues.cbz", 128, null,
            [new("half", EntityKind.ComicInstallment, "Special", IssueLabel: "½"), new("fraction", EntityKind.ComicInstallment, "Interlude", IssueLabel: "12.5")])], DateTimeOffset.UtcNow);
        ManagedLibraryService.ValidateSnapshot(input, snapshot);
        foreach (var invalid in new[] {
            snapshot.Files[0].Targets[0] with { IssueLabel = null },
            snapshot.Files[0].Targets[0] with { IssueLabel = "bad\nlabel" },
            snapshot.Files[0].Targets[0] with { IssueLabel = new string('a', 129) },
            snapshot.Files[0].Targets[0] with { EntityKind = EntityKind.VideoEpisode },
            snapshot.Files[0].Targets[0] with { SeasonNumber = 1 }
        }) Assert.Throws<IntegrationInvocationException>(() => ManagedLibraryService.ValidateSnapshot(input,
            snapshot with { Files = [snapshot.Files[0] with { Targets = [invalid] }] }));
    }

    [Fact]
    public async Task ReadHoldingPinsIdentityAndReturnsRemoteEvidenceWithoutImporting() {
        var fixture = new Fixture();
        var page = await fixture.Service.SearchAsync(fixture.Connection.State.Id, new(EntityKind.Movie), default);
        var item = Assert.Single(page.Items);
        var snapshot = await fixture.Service.GetAsync(fixture.Connection.State.Id, new(item.EntityKind, item.RemoteId, item.ExternalIds), default);
        Assert.Single(snapshot.Files);
        fixture.Snapshot = snapshot with { Item = item with { ExternalIds = new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "other" } } };
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.Service.GetAsync(fixture.Connection.State.Id, new(item.EntityKind, item.RemoteId, item.ExternalIds), default));
    }

    [Fact]
    public async Task RevokedManifestCannotReadSecretsOrExecuteLibraryOperations() {
        var fixture = new Fixture();
        fixture.Manifest = fixture.Manifest with { Integration = new(1, [], []) };
        await Assert.ThrowsAsync<ConnectionCapabilityUnavailableException>(() => fixture.Service.SearchAsync(fixture.Connection.State.Id, new(EntityKind.Movie), default));
        Assert.Equal(0, fixture.SecretReads);
        Assert.Equal(0, fixture.Calls);
    }

    [Fact]
    public async Task OversizedPagesAndDuplicateFileAssociationsAreRejected() {
        var fixture = new Fixture();
        fixture.Page = new([fixture.Snapshot.Item, fixture.Snapshot.Item]);
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.Service.SearchAsync(fixture.Connection.State.Id, new(EntityKind.Movie, Limit: 1), default));
        fixture.Snapshot = fixture.Snapshot with { Files = [fixture.Snapshot.Files[0], fixture.Snapshot.Files[0]] };
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.Service.GetAsync(fixture.Connection.State.Id,
            new(EntityKind.Movie, fixture.Snapshot.Item.RemoteId, fixture.Snapshot.Item.ExternalIds), default));
    }

    [Fact]
    public async Task UnknownFileCountIsPreservedAndOptionsStayExternal() {
        var fixture = new Fixture();
        fixture.Page = new([fixture.Snapshot.Item with { RemoteFileCount = null }]);
        Assert.Null(Assert.Single((await fixture.Service.SearchAsync(fixture.Connection.State.Id, new(EntityKind.Movie), default)).Items).RemoteFileCount);
        var options = await fixture.Service.OptionsAsync(fixture.Connection.State.Id, new(EntityKind.Movie), default);
        Assert.Equal("external-profile", Assert.Single(options.Profiles).Id);
    }

    [Fact]
    public async Task LegacyItemsWithoutPresentationRemainValid() {
        var fixture = new Fixture();

        var item = Assert.Single((await fixture.Service.SearchAsync(fixture.Connection.State.Id, new(EntityKind.Movie), default)).Items);
        var snapshot = await fixture.Service.GetAsync(fixture.Connection.State.Id,
            new(item.EntityKind, item.RemoteId, item.ExternalIds), default);
        var legacyJson = JsonSerializer.Serialize(new {
            item.RemoteId,
            item.EntityKind,
            item.Title,
            item.Year,
            item.ExternalIds,
            item.Monitored,
            item.ProfileId,
            item.RemoteFileCount,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var legacyItem = JsonSerializer.Deserialize<ManagedLibraryItem>(legacyJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Null(item.Presentation);
        Assert.Null(snapshot.Item.Presentation);
        Assert.NotNull(legacyItem);
        Assert.Null(legacyItem.Presentation);
    }

    [Fact]
    public async Task SourcePresentationIsReturnedWithoutCreatingLibraryMetadata() {
        var fixture = new Fixture();
        var presentation = new ManagedLibraryPresentation(
            "First line\nSecond line\twith context.",
            "https://images.example.test/poster.jpg?width=500",
            "http://images.example.test/backdrop.jpg",
            ["Science Fiction", "Drama"],
            116,
            "PG-13");
        var item = fixture.Snapshot.Item with { Presentation = presentation };
        fixture.Page = new([item]);
        fixture.Snapshot = fixture.Snapshot with { Item = item };

        var pageItem = Assert.Single((await fixture.Service.SearchAsync(fixture.Connection.State.Id, new(EntityKind.Movie), default)).Items);
        var snapshot = await fixture.Service.GetAsync(fixture.Connection.State.Id,
            new(item.EntityKind, item.RemoteId, item.ExternalIds), default);

        Assert.Equal(presentation, pageItem.Presentation);
        Assert.Equal(presentation, snapshot.Item.Presentation);
    }

    [Fact]
    public async Task InvalidSourcePresentationIsRejectedAtTheIntegrationBoundary() {
        var fixture = new Fixture();
        var invalidPresentations = new ManagedLibraryPresentation[] {
            new(Overview: "bad\u0000overview"),
            new(Overview: new string('a', 32_769)),
            new(PosterUrl: "javascript:alert(1)"),
            new(PosterUrl: "https://user:secret@images.example.test/poster.jpg"),
            new(BackdropUrl: "https://images.example.test/backdrop.jpg#fragment"),
            new(Genres: Enumerable.Range(0, 65).Select(index => $"Genre {index}").ToArray()),
            new(Genres: ["Drama", "Drama"]),
            new(Genres: ["bad\nlabel"]),
            new(RuntimeMinutes: 0),
            new(RuntimeMinutes: 10_081),
            new(ContentRating: "PG\n13"),
        };

        foreach (var presentation in invalidPresentations) {
            fixture.Page = new([fixture.Snapshot.Item with { Presentation = presentation }]);
            await Assert.ThrowsAsync<IntegrationInvocationException>(() => fixture.Service.SearchAsync(
                fixture.Connection.State.Id, new(EntityKind.Movie), default));
        }
    }

    private sealed class Fixture : IIntegrationConnectionStore, IIntegrationPluginGateway, IIntegrationManagerGateway {
        private const string PluginId = "fixture-manager";
        internal IntegrationConnection Connection { get; }
        internal PluginManifest Manifest { get; set; }
        internal ManagedLibraryPage Page { get; set; }
        internal ManagedItemSnapshot Snapshot { get; set; }
        internal ManagedLibraryService Service { get; }
        internal int SecretReads { get; private set; }
        internal int Calls { get; private set; }
        internal IReadOnlyDictionary<string, string> Secrets { get; set; } = new Dictionary<string, string>();
        internal IReadOnlyCollection<string>? RequestedSecretKeys { get; private set; }
        internal IReadOnlyDictionary<string, string>? LastAuth { get; private set; }
        internal Fixture() {
            var support = new IntegrationSupport[] {
                new(PluginCapability.ConnectedLibrary, [IntegrationOperation.SearchLibrary, IntegrationOperation.GetLibraryItem], [EntityKind.Movie]),
                new(PluginCapability.ExternalManager, [IntegrationOperation.ManagerOptions], [EntityKind.Movie])
            };
            Connection = IntegrationConnection.Create(PluginId, "Manager", "http://manager.test/", true, support.Select(value => value.Kind).ToArray(), new Dictionary<string, string>());
            Connection.RecordProbe("installation", support, null, DateTimeOffset.UtcNow);
            Manifest = new(2, [], PluginId, "Manager", "1.0.0", "dotnet-process", "plugin.dll", new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, support.Select(value => new PluginIntegrationCapability(value.Kind, value.Operations, value.EntityKinds)).ToArray(), []));
            var item = new ManagedLibraryItem("1", EntityKind.Movie, "Movie", 2024, new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "1001" }, false, "external-profile", 1);
            Page = new([item]);
            Snapshot = new(item, "/remote/movie", [new("11", "/remote/movie/file.mkv", 128, null, [new("1", EntityKind.Movie, "Movie")])], DateTimeOffset.UtcNow);
            Service = new(new(this, this), this);
        }
        public Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<StoredIntegrationConnection?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<StoredIntegrationConnection?>(id == Connection.State.Id ? new(Connection, []) : null);
        public Task SaveAsync(IntegrationConnection connection, long? expectedRevision, IReadOnlyDictionary<string, string?> secretChanges, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyDictionary<string, string>> ReadSecretsAsync(Guid id, IReadOnlyCollection<string> credentialKeys, CancellationToken cancellationToken) { SecretReads++; RequestedSecretKeys = credentialKeys; return Task.FromResult(Secrets); }
        public Task DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PluginManifest?> FindAsync(string pluginId, CancellationToken cancellationToken) => Task.FromResult<PluginManifest?>(Manifest);
        public Task<ConnectionProbeResult> ProbeAsync(string pluginId, IntegrationConnectionContext connection, CancellationToken cancellationToken) { LastAuth = connection.Auth; return Task.FromResult(new ConnectionProbeResult("installation", "Fixture", "1.0.0", Manifest.Integration!.Capabilities)); }
        public Task<ManagedLibraryPage> SearchLibraryAsync(string pluginId, IntegrationConnectionContext connection, ManagedLibraryQuery input, CancellationToken cancellationToken) { Calls++; LastAuth = connection.Auth; return Task.FromResult(Page); }
        public Task<ManagedItemSnapshot> GetLibraryItemAsync(string pluginId, IntegrationConnectionContext connection, ManagedItemInput input, CancellationToken cancellationToken) { Calls++; return Task.FromResult(Snapshot); }
        public Task<ManagerOptions> GetOptionsAsync(string pluginId, IntegrationConnectionContext connection, ManagerOptionsInput input, CancellationToken cancellationToken) =>
            Task.FromResult(new ManagerOptions([new("external-profile", "Existing profile")], [new("root", "/remote", true)]));
    }
}
