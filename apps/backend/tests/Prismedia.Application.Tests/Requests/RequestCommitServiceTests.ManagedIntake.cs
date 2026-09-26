using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Integrations;
using Prismedia.Application.Plugins;
using Prismedia.Application.Requests;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests.Requests;

public sealed partial class RequestCommitServiceTests {
    [Fact]
    public async Task ReviewedMovieCommitsTheWorkAndOneScopeInOneTransaction() {
        var fixture = new IntakeFixture();
        var request = ManagedMovieReview();
        var movieId = FakeWantedEntityWriter.EntityIdFor("19");
        fixture.Works[movieId] = new(EntityKind.Movie, new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "19" });

        var review = await fixture.Service.ReviewAsync(fixture.ConnectionId, new([new()], Request: request), default);
        var scope = Assert.Single(review.Scopes);
        Assert.Equal(fixture.LibraryMount.LibraryRootId, scope.Mount.LibraryRootId);
        Assert.Null(scope.Rendition);
        Assert.Empty(fixture.Store.Created);

        var result = await fixture.Service.CommitAsync(fixture.ConnectionId, new(
            Guid.NewGuid(), review.ConnectionRevision, [new(LibraryRootId: scope.Mount.LibraryRootId)],
            "profile-1", Monitored: false, Search: true, Request: request), default);

        Assert.Equal(movieId, result.EntityId);
        var accepted = Assert.Single(result.Scopes);
        Assert.NotNull(accepted.ManagedRequest);
        Assert.Null(accepted.Error);
        var stored = Assert.Single(fixture.Store.Created);
        Assert.Equal("profile-1", stored.Plan.Request.ProfileId);
        Assert.Equal(1, fixture.CommitScopes);
        Assert.Single(fixture.Writer.Ensured);
    }

    [Fact]
    public async Task ReviewedBookRequestsEachRenditionSeparatelyAndRetriesOnlyTheRefusedOne() {
        var fixture = new IntakeFixture();
        var request = ManagedBookReview();
        var bookId = FakeWantedEntityWriter.EntityIdFor("OL43053199W");
        fixture.Works[bookId] = new(EntityKind.Book, new Dictionary<string, string> { [ExternalIdProviders.OpenLibraryWork] = "OL43053199W" });
        fixture.Store.RefusedRenditions.Add(BookRendition.Audiobook);
        var scopes = new ManagedRequestScopeChoice[] {
            new(BookRendition.Ebook, fixture.LibraryMount.LibraryRootId),
            new(BookRendition.Audiobook, fixture.AudiobookMount.LibraryRootId)
        };

        var review = await fixture.Service.ReviewAsync(fixture.ConnectionId, new(scopes, Request: request), default);
        Assert.Equal([BookRendition.Ebook, BookRendition.Audiobook], review.Scopes.Select(scope => scope.Rendition));
        Assert.Equal(fixture.AudiobookMount.Id, review.Scopes[1].Mount.Id);

        var operationId = Guid.NewGuid();
        var input = new CommitReviewedManagedRequestInput(operationId, review.ConnectionRevision, scopes,
            null, Monitored: true, Search: true, Request: request);
        var first = await fixture.Service.CommitAsync(fixture.ConnectionId, input, default);

        Assert.Equal(bookId, first.EntityId);
        Assert.NotNull(first.Scopes[0].ManagedRequest);
        Assert.Null(first.Scopes[1].ManagedRequest);
        Assert.NotNull(first.Scopes[1].Error);
        Assert.Single(fixture.Store.Created);
        Assert.Single(fixture.Writer.Ensured);

        fixture.Store.RefusedRenditions.Clear();
        var retry = await fixture.Service.CommitAsync(fixture.ConnectionId, input, default);

        Assert.Equal(first.Scopes[0].ManagedRequest!.Id, retry.Scopes[0].ManagedRequest!.Id);
        Assert.NotNull(retry.Scopes[1].ManagedRequest);
        Assert.NotEqual(retry.Scopes[0].ManagedRequest!.Id, retry.Scopes[1].ManagedRequest!.Id);
        Assert.Equal(2, fixture.Store.Created.Count);
        Assert.Equal(BookRendition.Audiobook, fixture.Store.Created[1].Plan.Request.ReviewedWork.BookRendition);
    }

    [Fact]
    public async Task ConnectedIssueReviewNamesTheExactTargetAndCommitsOneScopedRequest() {
        var fixture = new IntakeFixture();
        var connected = new ManagedConnectedTargetInput(fixture.RunItem, "issue-2", "2");

        var review = await fixture.Service.ReviewAsync(fixture.ConnectionId, new([new()], Connected: connected), default);
        var scope = Assert.Single(review.Scopes);
        var target = Assert.Single(review.Targets!);
        Assert.Equal("2", target.Label);
        Assert.Equal("4000-2002", target.ExternalIds![ExternalIdProviders.ComicVine]);
        Assert.Equal("2", Assert.Single(scope.Work.Targets!).IssueLabel);
        Assert.NotNull(scope.Existing);
        Assert.Equal(fixture.LibraryMount.Id, scope.Mount.Id);

        var result = await fixture.Service.CommitAsync(fixture.ConnectionId, new(
            Guid.NewGuid(), review.ConnectionRevision, [new(LibraryRootId: scope.Mount.LibraryRootId)],
            null, Monitored: true, Search: true, Connected: connected), default);

        Assert.Equal(fixture.SeriesId, result.EntityId);
        var accepted = Assert.Single(result.Scopes);
        Assert.Equal([fixture.IssueId], accepted.TargetEntityIds!);
        Assert.NotNull(accepted.ManagedRequest);
        Assert.Equal(1, fixture.IssueWrites);
        var stored = Assert.Single(fixture.Store.Created);
        Assert.Equal([fixture.IssueId], stored.Plan.Request.TargetEntityIds!);
        Assert.True(stored.Plan.Request.Search);
    }

    [Fact]
    public async Task ExistingWantedEntityCommitsFromTheStoreTargetWithoutMaterializing() {
        var fixture = new IntakeFixture();
        var movieId = Guid.NewGuid();
        fixture.Works[movieId] = new(EntityKind.Movie, new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "19" });
        var scopes = new ManagedRequestScopeChoice[] { new(LibraryRootId: fixture.LibraryMount.LibraryRootId) };

        var review = await fixture.Service.ReviewAsync(fixture.ConnectionId, new(scopes, EntityId: movieId), default);
        Assert.Equal(movieId, review.EntityId);
        Assert.Equal("Stored title", review.Title);

        var result = await fixture.Service.CommitAsync(fixture.ConnectionId, new(
            Guid.NewGuid(), review.ConnectionRevision, scopes, "profile-1", Monitored: false, Search: true,
            EntityId: movieId), default);

        Assert.Equal(movieId, result.EntityId);
        Assert.NotNull(Assert.Single(result.Scopes).ManagedRequest);
        Assert.Empty(fixture.Writer.Ensured);
        Assert.Single(fixture.Store.Created);
    }

    [Fact]
    public async Task ReviewRefusesAmbiguousSourcesAndWrongScopes() {
        var fixture = new IntakeFixture();
        var request = ManagedMovieReview();

        await Assert.ThrowsAsync<RequestCommitValidationException>(() => fixture.Service.ReviewAsync(
            fixture.ConnectionId, new([new()], EntityId: Guid.NewGuid(), Request: request), default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.ReviewAsync(
            fixture.ConnectionId, new([new(BookRendition.Ebook)], Request: request), default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.ReviewAsync(
            fixture.ConnectionId, new([new(), new()], Request: request), default));
        Assert.Empty(fixture.Store.Created);
    }

    /// <summary>One connection that manages movies, Books, and comic runs, with two mapped libraries.</summary>
    private sealed class IntakeFixture : IIntegrationConnectionStore, IIntegrationPluginGateway, IIntegrationManagerGateway,
        IIntegrationManagerCreationGateway, IIntegrationLibraryGateway, IExternalLibraryMountStore, IManagedTrackingStore,
        IReviewedFulfillmentOwnershipReader, IReviewedManagedRequestCommitScope, IManagedComicIssueWriter,
        ISettingsPersistence, IPluginRequestReviewSource, IIdentifyProviderService, IPluginIdentityRouter {
        private const string PluginId = "fixture-manager";
        private readonly IntegrationConnection _connection;
        private readonly PluginManifest _manifest;
        private readonly LibraryRoot[] _roots;

        internal IntakeFixture() {
            IntegrationSupport[] support = [
                new(PluginCapability.ExternalManager,
                    [IntegrationOperation.LookupManaged, IntegrationOperation.EnsureManaged, IntegrationOperation.ReconcileManaged,
                        IntegrationOperation.ConfigureManaged, IntegrationOperation.RequestManaged, IntegrationOperation.ManagerOptions],
                    [EntityKind.Movie, EntityKind.Book, EntityKind.ComicSeries]),
                new(PluginCapability.ConnectedLibrary,
                    [IntegrationOperation.GetLibraryItem, IntegrationOperation.ListLibraries],
                    [EntityKind.Movie, EntityKind.Book, EntityKind.ComicSeries])
            ];
            _connection = IntegrationConnection.Create(PluginId, "Manager", "https://manager.test", true,
                [PluginCapability.ExternalManager, PluginCapability.ConnectedLibrary], new Dictionary<string, string>());
            _connection.RecordProbe(null, support, null, DateTimeOffset.UtcNow, false);
            _manifest = new(2, [], PluginId, "Manager", "1.0.0", "dotnet-process", "plugin.dll",
                new("2.0.0", null, "3.8.0", null), [], false, [],
                Integration: new(1, support.Select(value => new PluginIntegrationCapability(
                    value.Kind, value.Operations, value.EntityKinds)).ToArray(), []));
            var now = DateTimeOffset.UtcNow;
            LibraryMount = new(Guid.NewGuid(), ConnectionId, Guid.NewGuid(), "root-1", "/remote/library", "/media/library", "Library");
            AudiobookMount = new(Guid.NewGuid(), ConnectionId, Guid.NewGuid(), "root-2", "/remote/audiobooks", "/media/audiobooks", "Audiobooks");
            _roots = [Root(LibraryMount.LibraryRootId, "/media/library"), Root(AudiobookMount.LibraryRootId, "/media/audiobooks")];
            Store = new(this);
            SeriesId = Guid.NewGuid();
            IssueId = Guid.NewGuid();
            RunItem = new(EntityKind.ComicSeries, "run-1",
                new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4050-1001" });
            Works[SeriesId] = new(EntityKind.ComicSeries,
                new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4050-1001" },
                [new(EntityKind.ComicInstallment, new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-2002" }, IssueLabel: "2")]);

            var access = new IntegrationConnectionAccess(this, this);
            var managedLibrary = new ManagedLibraryService(access, this);
            var settings = new SettingsService(this);
            var externalLibraries = new ExternalLibraryService(this, managedLibrary, new ProviderLibraryService(this, access, this), access, settings);
            var movies = new ReviewedWantedMovieService(Writer, new FakeSuppressionStore(), new MovieRouter(), new MovieLease());
            var books = new ReviewedWantedBookService(Writer, new FakeSuppressionStore(), new BookRouter(), new MovieLease());
            var discovery = new ManagedDiscoveryService(access, this, this, movies, this, this, this);
            var requests = new ManagedRequestService(Store, access, this);
            Service = new(discovery, [movies, books], [new ReviewedWantedComicIssueService(this)], access, this,
                managedLibrary, externalLibraries, this, this, Store, requests, this,
                NullLogger<ReviewedManagedRequestService>.Instance);
        }

        internal Guid ConnectionId => _connection.State.Id;
        internal ExternalLibraryMount LibraryMount { get; }
        internal ExternalLibraryMount AudiobookMount { get; }
        internal FakeWantedEntityWriter Writer { get; } = new();
        internal IntakeStore Store { get; }
        internal ReviewedManagedRequestService Service { get; }
        internal Dictionary<Guid, ManagedLookupInput> Works { get; } = [];
        internal List<ReviewedFulfillmentOwnership> Ownerships { get; } = [];
        internal ManagedItemInput RunItem { get; }
        internal Guid SeriesId { get; }
        internal Guid IssueId { get; }
        internal int CommitScopes { get; private set; }
        internal int IssueWrites { get; private set; }

        private static LibraryRoot Root(Guid id, string path) => new(id, path, path, true, true, true, true, true, true, false,
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        private ManagedItemSnapshot RunSnapshot() => new(
            new("run-1", EntityKind.ComicSeries, "Atomic Attack", 1954, RunItem.ExpectedExternalIds, false, null, 0),
            "/remote/library/Atomic Attack", [], DateTimeOffset.UtcNow,
            [new("issue-1", "1", "Issue 1", true, new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-2001" }),
                new("issue-2", "2", "Issue 2", false, new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-2002" })]);

        public Task<ManagedLookupResult> LookupAsync(string pluginId, IntegrationConnectionContext connection, ManagedLookupInput input, CancellationToken token) {
            var candidate = new ManagedCandidate(input.EntityKind, "Candidate", 2000, input.ExternalIds);
            return Task.FromResult(input.EntityKind == EntityKind.ComicSeries
                ? new ManagedLookupResult(candidate, RunSnapshot(),
                    [new("issue-2", EntityKind.ComicInstallment, new Dictionary<string, string> { [ExternalIdProviders.ComicVine] = "4000-2002" }, IssueLabel: "2")])
                : new ManagedLookupResult(candidate, null));
        }
        public Task<EnsureManagedResult> EnsureAsync(string pluginId, IntegrationConnectionContext connection, EnsureManagedInput input, CancellationToken token) => throw new NotImplementedException();
        public Task<ManagedItemSnapshot> GetLibraryItemAsync(string pluginId, IntegrationConnectionContext connection, ManagedItemInput input, CancellationToken token) =>
            Task.FromResult(RunSnapshot());
        public Task<ManagerOptions> GetOptionsAsync(string pluginId, IntegrationConnectionContext connection, ManagerOptionsInput input, CancellationToken token) =>
            Task.FromResult(new ManagerOptions([new("profile-1", "Balanced")],
                [new("root-1", "/remote/library", true), new("root-2", "/remote/audiobooks", true)]));
        public Task<ManagedLibraryPage> SearchLibraryAsync(string pluginId, IntegrationConnectionContext connection, ManagedLibraryQuery input, CancellationToken token) => throw new NotImplementedException();
        public Task<ManagedDiscoveryPage> DiscoverAsync(string pluginId, IntegrationConnectionContext connection, ManagedDiscoveryQuery input, CancellationToken token) => throw new NotImplementedException();
        public Task<ProviderLibraryCatalog> ListLibrariesAsync(string pluginId, IntegrationConnectionContext connection, CancellationToken token) =>
            Task.FromResult(new ProviderLibraryCatalog([
                new("root-1", "Library", "/remote/library", [EntityKind.Movie, EntityKind.Book, EntityKind.ComicSeries]),
                new("root-2", "Audiobooks", "/remote/audiobooks", [EntityKind.Book])]));
        public Task<StoredIntegrationConnection?> FindAsync(Guid id, CancellationToken token) =>
            Task.FromResult<StoredIntegrationConnection?>(id == ConnectionId ? new(_connection, []) : null);
        public Task<IReadOnlyDictionary<string, string>> ReadSecretsAsync(Guid id, IReadOnlyCollection<string> credentialKeys, CancellationToken token) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task<PluginManifest?> FindAsync(string pluginId, CancellationToken token) => Task.FromResult<PluginManifest?>(pluginId == PluginId ? _manifest : null);
        public Task<ConnectionProbeResult> ProbeAsync(string pluginId, IntegrationConnectionContext connection, CancellationToken token) => throw new NotImplementedException();
        public Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<StoredIntegrationConnection>>([new(_connection, [])]);
        public Task SaveAsync(IntegrationConnection connection, long? revision, IReadOnlyDictionary<string, string?> secrets, CancellationToken token) => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, long revision, CancellationToken token) => throw new NotImplementedException();
        public Task<IReadOnlySet<Guid>> ListMountedLibraryRootIdsAsync(CancellationToken token) => throw new NotImplementedException();
        Task<IReadOnlyList<ExternalLibraryMount>> IExternalLibraryMountStore.ListAsync(Guid connectionId, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ExternalLibraryMount>>([LibraryMount, AudiobookMount]);
        public Task<ExternalLibraryMount> CreateAsync(Guid connectionId, long expectedRevision, CreateExternalLibraryMountRequest request, CancellationToken token) => throw new NotImplementedException();
        public Task<ExternalLibraryMount> AttachAsync(Guid connectionId, long expectedRevision, AttachExistingExternalLibraryMountRequest request, CancellationToken token) => throw new NotImplementedException();
        public Task<ExternalLibraryMountAttachment> AttachWithResultAsync(Guid connectionId, long expectedRevision, AttachExistingExternalLibraryMountRequest request, CancellationToken token) => throw new NotImplementedException();
        public Task<IReadOnlyList<MappedLibraryFile>> InspectAsync(Guid connectionId, IReadOnlyList<ManagedLibraryFile> files, CancellationToken token) => throw new NotImplementedException();
        Task<ManagedTrackingWork?> IManagedTrackingStore.FindAsync(Guid id, CancellationToken token) => Task.FromResult<ManagedTrackingWork?>(null);
        Task<IReadOnlyList<ManagedTrackingResponse>> IManagedTrackingStore.ListAsync(Guid connectionId, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ManagedTrackingResponse>>([]);
        public Task<ManagedTrackingObservation> ObserveAsync(Guid connectionId, ManagedItemSnapshot snapshot, CancellationToken token) => throw new NotImplementedException();
        public Task<ManagedTrackingResponse> CreateAsync(Guid connectionId, TrackManagedHoldingRequest request, string title, CancellationToken token) => throw new NotImplementedException();
        public Task ApplyAsync(ManagedTrackingWork work, ManagedTrackingObservation observation, IReadOnlyList<ManagedFileBinding>? adoption, IReadOnlyList<ManagedSourceChange> changes, CancellationToken token) => throw new NotImplementedException();
        public Task ConfirmRemovalAsync(ManagedTrackingWork work, string problem, CancellationToken token) => throw new NotImplementedException();
        public Task RequireReviewAsync(Guid id, long revision, string problem, CancellationToken token) => throw new NotImplementedException();
        public Task RecordUnverifiableAsync(Guid id, long revision, CancellationToken token) => throw new NotImplementedException();
        public Task RecordReappearanceAsync(Guid id, long revision, string problem, CancellationToken token) => throw new NotImplementedException();
        public Task QueueAsync(Guid connectionId, Guid id, CancellationToken token) => throw new NotImplementedException();
        public Task QueueDueAsync(CancellationToken token) => throw new NotImplementedException();
        Task<IReadOnlyList<ReviewedFulfillmentOwnership>> IReviewedFulfillmentOwnershipReader.ListAsync(ManagedLookupInput work, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ReviewedFulfillmentOwnership>>(Ownerships.ToArray());
        public async Task<T> ExecuteAsync<T>(Guid connectionId, long expectedConnectionRevision, Func<CancellationToken, Task<T>> action, CancellationToken token) {
            Assert.Equal(ConnectionId, connectionId);
            CommitScopes++;
            return await action(token);
        }
        public Task<(Guid SeriesEntityId, Guid IssueEntityId, bool HasFile)> EnsureAsync(ExternalIdentity seriesIdentity, string seriesTitle,
            ExternalIdentity issueIdentity, string issueTitle, string issueLabel, CancellationToken token) {
            Assert.Equal("4050-1001", seriesIdentity.Value);
            Assert.Equal("4000-2002", issueIdentity.Value);
            Assert.Equal("2", issueLabel);
            IssueWrites++;
            return Task.FromResult((SeriesId, IssueId, false));
        }
        public Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LibraryRoot>>(_roots);
        public Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        public Task SaveSettingOverrideAsync(string key, string valueJson, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveSettingOverridesAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceSettingOverridesAsync(IReadOnlyDictionary<string, string> upserts, IReadOnlyCollection<string> deletes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteSettingOverrideAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_roots.FirstOrDefault(root => root.Id == id));
        public Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RequestReviewResponse?> ReviewAsync(RequestReviewRequest request, bool hideNsfw, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The intake must not re-review metadata.");
        public Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(string? entityKind, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PluginProvider>>([]);
        public Task<IdentifyPluginResponse> IdentifyAsync(Guid entityId, string providerId, IdentifyQuery? query,
            IReadOnlyDictionary<string, string>? parentExternalIds, bool hideNsfw, CancellationToken cancellationToken,
            bool cascadeChildren = true, IIdentifyCascadeSink? sink = null, bool hydrateRelationships = true) => throw new NotImplementedException();
        public Task<bool> ApplyAsync(Guid entityId, EntityMetadataProposal proposal, IReadOnlyCollection<string> selectedFields,
            IReadOnlyDictionary<string, string?>? selectedImages, CancellationToken cancellationToken,
            IIdentifyApplyProgressReporter? progress = null) => throw new NotImplementedException();
        public Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(string kind, IdentifyAction action, IReadOnlyList<ExternalIdentity> identities, CancellationToken token) =>
            throw new InvalidOperationException("The intake must not route as metadata.");

        /// <summary>Request store that derives targets from the fixture's known works and can refuse one rendition.</summary>
        internal sealed class IntakeStore(IntakeFixture fixture) : IManagedRequestStore {
            internal List<StoredManagedRequest> Created { get; } = [];
            internal HashSet<BookRendition> RefusedRenditions { get; } = [];

            public Task<ManagedRequestTarget> RequireTargetAsync(Guid connectionId, Guid entityId, Guid libraryRootId,
                IReadOnlyList<Guid>? targetEntityIds, BookRendition? bookRendition, CancellationToken token) {
                if (bookRendition is { } rendition && RefusedRenditions.Contains(rendition)) {
                    throw new ArgumentException("This rendition already has a file.");
                }

                var work = fixture.Works[entityId] with { BookRendition = bookRendition };
                var mount = new[] { fixture.LibraryMount, fixture.AudiobookMount }.Single(candidate => candidate.LibraryRootId == libraryRootId);
                var targets = targetEntityIds is null ? null
                    : targetEntityIds.Select((id, index) => new ManagedRequestEntityTarget(id, work.Targets![index])).ToArray();
                return Task.FromResult(new ManagedRequestTarget(entityId, "Stored title", work, mount, targets));
            }
            public Task<StoredManagedRequest?> FindAsync(Guid id, CancellationToken token) =>
                Task.FromResult(Created.FirstOrDefault(request => request.Operation.State.OperationId == id));
            public Task<IReadOnlyList<StoredManagedRequest>> ListAsync(Guid connectionId, CancellationToken token) =>
                Task.FromResult<IReadOnlyList<StoredManagedRequest>>(Created.ToArray());
            public Task<StoredManagedRequest> CreateAsync(ManagedRequestOperation operation, ManagedRequestPlan plan, CancellationToken token) {
                var stored = new StoredManagedRequest(operation, plan, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
                Created.Add(stored);
                return Task.FromResult(stored);
            }
            public Task SaveAsync(ManagedRequestOperation operation, long expectedRevision, string? problem, bool beforeDispatch, CancellationToken token) => throw new NotImplementedException();
            public Task AcceptHoldingAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, IReadOnlyList<ManagedResolvedTarget>? resolvedTargets, CancellationToken token) => throw new NotImplementedException();
            public Task ValidateHoldingAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token) => throw new NotImplementedException();
            public Task<ManagedRequestMaterialization> MaterializeAsync(StoredManagedRequest work, ManagedItemSnapshot snapshot, CancellationToken token) => throw new NotImplementedException();
            public Task QueueAsync(Guid id, CancellationToken token) => throw new NotImplementedException();
            public Task QueueDueAsync(CancellationToken token) => throw new NotImplementedException();
        }
    }
}
