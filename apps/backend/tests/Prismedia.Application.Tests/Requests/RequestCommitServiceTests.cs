using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Requests;

/// <summary>
/// Covers the registry-driven request commit — the Identify apply run on entities that don't exist on
/// disk yet, for every requestable kind: containers (author, artist) create the wanted grouping plus
/// picked works with one acquisition each; leaves (book, movie, album) create themselves; owned and
/// in-flight picks are skipped transparently; non-committable kinds (series) are refused.
/// </summary>
public sealed class RequestCommitServiceTests {
    private const string Provider = "openlibrary";

    [Fact]
    public async Task AuthorCommitCreatesTheWantedAuthorAndPickedBooksAndAcquiresEach() {
        var proposal = Container(ProposalKind.Person, "Brandon Sanderson", "A1",
            Leaf(ProposalKind.Book, "Elantris", "W1"), Leaf(ProposalKind.Book, "Warbreaker", "W2"), Leaf(ProposalKind.Book, "Skipped", "W3"));
        var (service, writer, acquisitions) = Service(proposal);

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Author, $"{Provider}:A1", [$"{Provider}:W1", $"{Provider}:W2"]),
            hideNsfw: false, CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(response!.ContainerEntityId);

        var author = Assert.Single(writer.Ensured, call => call.Kind == EntityKind.BookAuthor);
        Assert.Equal("Brandon Sanderson", author.Title);
        Assert.True(author.MatchTitleKindWide);
        Assert.Null(author.ParentEntityId);

        var books = writer.Ensured.Where(call => call.Kind == EntityKind.Book).ToArray();
        Assert.Equal(["W1", "W2"], books.Select(call => call.ItemId).ToArray());
        Assert.All(books, call => Assert.Equal(response.ContainerEntityId, call.ParentEntityId));
        Assert.All(books, call => Assert.False(call.MatchTitleKindWide));

        Assert.Equal(2, response.Items.Count);
        Assert.All(response.Items, item => Assert.Equal(RequestCommitOutcome.Requested, item.Outcome));
        Assert.Equal(2, acquisitions.Created.Count);
        Assert.All(acquisitions.Created, request => Assert.Equal("Brandon Sanderson", request.Author));
        Assert.All(acquisitions.Created, request => Assert.Equal(EntityKind.Book, request.Kind));
        Assert.Equal(
            response.Items.Select(item => item.EntityId).ToArray(),
            acquisitions.Created.Select(request => request.EntityId).ToArray());
    }

    [Fact]
    public async Task ArtistCommitCreatesTheWantedArtistAndAlbumAcquisitions() {
        var proposal = Container(ProposalKind.MusicArtist, "Daft Punk", "MB1",
            Leaf(ProposalKind.AudioLibrary, "Discovery", "R1"), Leaf(ProposalKind.AudioLibrary, "Homework", "R2"));
        var (service, writer, acquisitions) = Service(proposal);

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Artist, $"{Provider}:MB1", [$"{Provider}:R1"]),
            hideNsfw: false, CancellationToken.None);

        var artist = Assert.Single(writer.Ensured, call => call.Kind == EntityKind.MusicArtist);
        Assert.True(artist.MatchTitleKindWide);
        var album = Assert.Single(writer.Ensured, call => call.Kind == EntityKind.AudioLibrary);
        Assert.Equal("R1", album.ItemId);
        Assert.Equal(response!.ContainerEntityId, album.ParentEntityId);

        var created = Assert.Single(acquisitions.Created);
        Assert.Equal(EntityKind.AudioLibrary, created.Kind);
        Assert.Equal("Daft Punk", created.Author);
        Assert.Equal("Discovery", created.Title);
    }

    [Fact]
    public async Task MovieCommitCreatesAWantedMovieWithItsAcquisitionLinked() {
        var proposal = Leaf(ProposalKind.Movie, "Dune: Part Two", "M1") with {
            Patch = Patch("Dune: Part Two", "M1") with { Credits = [new CreditPatch("Denis Villeneuve", "director", null, null)] }
        };
        var (service, writer, acquisitions) = Service(proposal);

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Movie, $"{Provider}:M1", []),
            hideNsfw: false, CancellationToken.None);

        Assert.Null(response!.ContainerEntityId);
        var item = Assert.Single(response.Items);
        Assert.Equal(RequestCommitOutcome.Requested, item.Outcome);
        Assert.Equal(EntityKind.Movie, Assert.Single(writer.Ensured).Kind);

        var created = Assert.Single(acquisitions.Created);
        Assert.Equal(EntityKind.Movie, created.Kind);
        // No author concept for movies — the primary credit strengthens the release query instead.
        Assert.Equal("Denis Villeneuve", created.Author);
        Assert.Equal(item.EntityId, created.EntityId);
    }

    [Fact]
    public async Task SeriesCommitAcquiresSeasonPacksAndMaterializesEpisodePhantoms() {
        // The season node carries its episodes (a season lookup ships them); positions ride the patch.
        var episode1 = Leaf(ProposalKind.Video, "Pilot", "E1") with {
            Patch = Patch("Pilot", "E1", new Dictionary<string, int> { ["seasonNumber"] = 1, ["episodeNumber"] = 1 }),
        };
        var episode2 = Leaf(ProposalKind.Video, "Aftermath", "E2") with {
            Patch = Patch("Aftermath", "E2", new Dictionary<string, int> { ["seasonNumber"] = 1, ["episodeNumber"] = 2 }),
        };
        var season = Container(ProposalKind.VideoSeason, "Season 1", "S1", episode1, episode2) with {
            Patch = Patch("Season 1", "S1", new Dictionary<string, int> { ["seasonNumber"] = 1 }),
        };
        var (service, writer, acquisitions) = Service(Container(ProposalKind.VideoSeries, "Andor", "TV1", season));

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Series, $"{Provider}:TV1", [$"{Provider}:S1"]),
            hideNsfw: false, CancellationToken.None);

        Assert.NotNull(response);
        // One acquisition: the season pack, searched with series context and its season number.
        var created = Assert.Single(acquisitions.Created);
        Assert.Equal(EntityKind.VideoSeason, created.Kind);
        Assert.Equal("Andor", created.Series);
        Assert.Null(created.Author);
        Assert.Equal(1, created.SeasonNumber);
        Assert.Null(created.EpisodeNumber);

        // The picked season's episodes materialize as wanted phantoms beneath it — never acquisitions.
        var seasonEntityId = Assert.Single(writer.Ensured, call => call.Kind == EntityKind.VideoSeason);
        var episodes = writer.Ensured.Where(call => call.Kind == EntityKind.Video).ToArray();
        Assert.Equal(["E1", "E2"], episodes.Select(call => call.ItemId).ToArray());
        Assert.All(episodes, call => Assert.Equal(
            FakeWantedEntityWriter.EntityIdFor("S1"), call.ParentEntityId));
    }

    [Fact]
    public async Task OwnedPickIsReportedAlreadyOwnedAndNeitherAppliedNorAcquired() {
        var proposal = Container(ProposalKind.Person, "Author", "A1",
            Leaf(ProposalKind.Book, "Owned", "W1"), Leaf(ProposalKind.Book, "New", "W2"));
        var (service, writer, acquisitions) = Service(proposal);
        writer.ExistingWithFile.Add("W1");

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Author, $"{Provider}:A1", [$"{Provider}:W1", $"{Provider}:W2"]),
            hideNsfw: false, CancellationToken.None);

        Assert.Equal(
            [RequestCommitOutcome.AlreadyOwned, RequestCommitOutcome.Requested],
            response!.Items.Select(item => item.Outcome).ToArray());
        Assert.Equal("New", Assert.Single(acquisitions.Created).Title);

        // The owned work is excluded from the container apply so a request can't overwrite owned metadata.
        var applied = Assert.Single(writer.Applied);
        Assert.Equal("New", Assert.Single(applied.Proposal.Children).Patch.Title);
    }

    [Fact]
    public async Task InFlightWantedPickIsReportedAlreadyRequestedWithoutANewAcquisition() {
        var proposal = Container(ProposalKind.Person, "Author", "A1", Leaf(ProposalKind.Book, "InFlight", "W1"));
        var (service, writer, acquisitions) = Service(proposal);
        writer.ExistingWanted.Add("W1");
        acquisitions.EntitiesWithAcquisitions.Add(FakeWantedEntityWriter.EntityIdFor("W1"));

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Author, $"{Provider}:A1", [$"{Provider}:W1"]),
            hideNsfw: false, CancellationToken.None);

        Assert.Equal(RequestCommitOutcome.AlreadyRequested, Assert.Single(response!.Items).Outcome);
        Assert.Empty(acquisitions.Created);
    }

    [Fact]
    public async Task SeriesVolumePicksBecomeStandaloneWantedBooksStampedWithTheSeries() {
        var proposal = Leaf(ProposalKind.Book, "The Stormlight Archive", "W0") with {
            Children = [Leaf(ProposalKind.Book, "The Way of Kings", "V1"), Leaf(ProposalKind.Book, "Words of Radiance", "V2")]
        };
        var (service, writer, acquisitions) = Service(proposal);

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Book, $"{Provider}:W0", [$"{Provider}:V2"]),
            hideNsfw: false, CancellationToken.None);

        var item = Assert.Single(response!.Items);
        Assert.Equal("Words of Radiance", item.Title);

        var created = Assert.Single(acquisitions.Created);
        Assert.Equal("The Stormlight Archive", created.Series);
        Assert.Equal(item.EntityId, created.EntityId);

        // The volume is applied with its own child proposal, not the series root's.
        var applied = Assert.Single(writer.Applied);
        Assert.Equal("Words of Radiance", applied.Proposal.Patch.Title);
        Assert.Null(Assert.Single(writer.Ensured).ParentEntityId);
    }

    [Fact]
    public async Task CommitsAutoMonitorTheirAcquisitionsAndTheContainer() {
        var proposal = Container(ProposalKind.Person, "Author", "A1", Leaf(ProposalKind.Book, "New Book", "W1"));
        var (service, _, acquisitions, monitors) = ServiceWithMonitors(proposal);

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Author, $"{Provider}:A1", [$"{Provider}:W1"]),
            hideNsfw: false, CancellationToken.None);

        // The requested pick is hands-off: its acquisition is monitored until acquired, and the author
        // container is monitored for new works so future releases keep appearing.
        Assert.Single(monitors.AcquisitionMonitors);
        Assert.Single(acquisitions.Created);
        Assert.Equal([response!.ContainerEntityId!.Value], monitors.EntityMonitors.ToArray());
    }

    [Fact]
    public async Task ContainerSyncCreatesPhantomsWithoutAcquisitions() {
        var proposal = Container(ProposalKind.Person, "Author", "A1",
            Leaf(ProposalKind.Book, "Known", "W1"), Leaf(ProposalKind.Book, "Brand New", "W2"));
        var (service, writer, acquisitions, monitors) = ServiceWithMonitors(proposal);
        var authorEntityId = FakeWantedEntityWriter.EntityIdFor("A1");
        writer.Container = new MonitorableContainer(
            authorEntityId, EntityKind.BookAuthor, "Author", [new ProviderRef(Provider, "A1")]);
        writer.ExistingWanted.Add("W1"); // already tracked from an earlier request
        acquisitions.EntitiesWithAcquisitions.Add(FakeWantedEntityWriter.EntityIdFor("W1"));

        var synced = await service.SyncContainerAsync(authorEntityId, CancellationToken.None);

        Assert.True(synced);
        // Discovery materializes the missing work as a wanted phantom but never downloads on its own.
        Assert.Contains(writer.Ensured, call => call.ItemId == "W2");
        Assert.Empty(acquisitions.Created);
        Assert.Empty(monitors.AcquisitionMonitors);
    }

    [Theory]
    [InlineData(MonitorPreset.All)]
    [InlineData(MonitorPreset.Future)]
    public async Task ContainerSyncWithAnAutoMonitorPresetMaterializesNewWorks(MonitorPreset preset) {
        var proposal = Container(ProposalKind.Person, "Author", "A1", Leaf(ProposalKind.Book, "Brand New", "W2"));
        var (service, writer, acquisitions, monitors) = ServiceWithMonitors(proposal);
        var authorEntityId = FakeWantedEntityWriter.EntityIdFor("A1");
        writer.Container = new MonitorableContainer(authorEntityId, EntityKind.BookAuthor, "Author", [new ProviderRef(Provider, "A1")]);
        monitors.StoredPreset = preset;

        var synced = await service.SyncContainerAsync(authorEntityId, CancellationToken.None);

        Assert.True(synced);
        // All and Future both auto-monitor future works: the discovered work becomes a phantom (no download).
        Assert.Contains(writer.Ensured, call => call.ItemId == "W2");
        Assert.Empty(acquisitions.Created);
    }

    [Theory]
    [InlineData(MonitorPreset.Missing)]
    [InlineData(MonitorPreset.None)]
    [InlineData(MonitorPreset.FirstSeason)]
    [InlineData(MonitorPreset.LatestSeason)]
    public async Task ContainerSyncWithANonAutoMonitorPresetSkipsNewWorks(MonitorPreset preset) {
        var proposal = Container(ProposalKind.Person, "Author", "A1", Leaf(ProposalKind.Book, "Brand New", "W2"));
        var (service, writer, acquisitions, monitors) = ServiceWithMonitors(proposal);
        var authorEntityId = FakeWantedEntityWriter.EntityIdFor("A1");
        writer.Container = new MonitorableContainer(authorEntityId, EntityKind.BookAuthor, "Author", [new ProviderRef(Provider, "A1")]);
        monitors.StoredPreset = preset;

        var synced = await service.SyncContainerAsync(authorEntityId, CancellationToken.None);

        // The container is still touched (kept alive) but a newly discovered work is never materialized.
        Assert.True(synced);
        Assert.DoesNotContain(writer.Ensured, call => call.Kind == EntityKind.Book);
        Assert.Empty(acquisitions.Created);
    }

    [Fact]
    public async Task ContainerSyncNeverRecordsAPresetOnTheMonitor() {
        // A sync must never clobber the preset an explicit request chose — it passes null through to the store.
        var proposal = Container(ProposalKind.Person, "Author", "A1", Leaf(ProposalKind.Book, "Brand New", "W2"));
        var (service, writer, _, monitors) = ServiceWithMonitors(proposal);
        var authorEntityId = FakeWantedEntityWriter.EntityIdFor("A1");
        writer.Container = new MonitorableContainer(authorEntityId, EntityKind.BookAuthor, "Author", [new ProviderRef(Provider, "A1")]);
        monitors.StoredPreset = MonitorPreset.All;

        await service.SyncContainerAsync(authorEntityId, CancellationToken.None);

        Assert.Null(Assert.Single(monitors.EntityMonitorPresets));
    }

    [Fact]
    public async Task ContainerCommitWithAPresetAndNoSelectionDerivesTheSelectionAndRecordsThePreset() {
        // Season 2 already owned; a Missing preset requests only the unowned seasons and records Missing on
        // the monitor so future syncs do not auto-monitor new seasons.
        var season1 = Container(ProposalKind.VideoSeason, "Season 1", "S1") with {
            Patch = Patch("Season 1", "S1", new Dictionary<string, int> { ["seasonNumber"] = 1 }),
        };
        var season2 = Container(ProposalKind.VideoSeason, "Season 2", "S2") with {
            Patch = Patch("Season 2", "S2", new Dictionary<string, int> { ["seasonNumber"] = 2 }),
        };
        var season3 = Container(ProposalKind.VideoSeason, "Season 3", "S3") with {
            Patch = Patch("Season 3", "S3", new Dictionary<string, int> { ["seasonNumber"] = 3 }),
        };
        var (service, writer, acquisitions, monitors) = ServiceWithMonitors(
            Container(ProposalKind.VideoSeries, "Andor", "TV1", season1, season2, season3));
        writer.ExistingWithFile.Add("S2"); // the library already owns season 2

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Series, $"{Provider}:TV1", [], Preset: MonitorPreset.Missing),
            hideNsfw: false, CancellationToken.None);

        Assert.NotNull(response);
        // Missing derives every not-yet-owned season: S1 and S3 acquire, S2 is skipped as already owned.
        Assert.Equal([1, 3], acquisitions.Created.Select(request => request.SeasonNumber).ToArray());
        Assert.All(acquisitions.Created, request => Assert.Equal(EntityKind.VideoSeason, request.Kind));
        // The chosen preset sticks to the container monitor for future syncs.
        Assert.Equal(MonitorPreset.Missing, Assert.Single(monitors.EntityMonitorPresets));
    }

    [Fact]
    public async Task ContainerCommitWithAFuturePresetRequestsNothingNowButRecordsThePreset() {
        var season1 = Container(ProposalKind.VideoSeason, "Season 1", "S1") with {
            Patch = Patch("Season 1", "S1", new Dictionary<string, int> { ["seasonNumber"] = 1 }),
        };
        var (service, _, acquisitions, monitors) = ServiceWithMonitors(
            Container(ProposalKind.VideoSeries, "Andor", "TV1", season1));

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Series, $"{Provider}:TV1", [], Preset: MonitorPreset.Future),
            hideNsfw: false, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Empty(acquisitions.Created); // Future only establishes the container watch
        Assert.Equal(MonitorPreset.Future, Assert.Single(monitors.EntityMonitorPresets));
    }

    [Fact]
    public async Task ContainerCommitWithAnExplicitSelectionStillRecordsThePresetButKeepsTheSelection() {
        var season1 = Container(ProposalKind.VideoSeason, "Season 1", "S1") with {
            Patch = Patch("Season 1", "S1", new Dictionary<string, int> { ["seasonNumber"] = 1 }),
        };
        var season2 = Container(ProposalKind.VideoSeason, "Season 2", "S2") with {
            Patch = Patch("Season 2", "S2", new Dictionary<string, int> { ["seasonNumber"] = 2 }),
        };
        var (service, _, acquisitions, monitors) = ServiceWithMonitors(
            Container(ProposalKind.VideoSeries, "Andor", "TV1", season1, season2));

        // An explicit pick of only S2 wins over the preset's derived selection; the preset is still recorded.
        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Series, $"{Provider}:TV1", [$"{Provider}:S2"], Preset: MonitorPreset.All),
            hideNsfw: false, CancellationToken.None);

        Assert.NotNull(response);
        var created = Assert.Single(acquisitions.Created);
        Assert.Equal(2, created.SeasonNumber);
        Assert.Equal(MonitorPreset.All, Assert.Single(monitors.EntityMonitorPresets));
    }

    [Fact]
    public async Task RequestEntitySkipsNonPluginIdentifiersAndReusesTheEntity() {
        var proposal = Leaf(ProposalKind.Book, "The Martian", "W1");
        var (service, writer, acquisitions, _) = ServiceWithMonitors(proposal);
        var phantomId = Guid.NewGuid();
        // The cascade stamps every provider identity, plugin or not — the isbn must be tried and skipped.
        writer.Container = new MonitorableContainer(
            phantomId, EntityKind.Book, "The Martian", [new ProviderRef("isbn13", "9780000000000"), new ProviderRef(Provider, "W1")]);

        var response = await service.RequestEntityAsync(phantomId, hideNsfw: true, CancellationToken.None);

        var item = Assert.Single(response!.Items);
        Assert.Equal(RequestCommitOutcome.Requested, item.Outcome);
        Assert.Equal($"{Provider}:W1", item.ExternalId);
        Assert.Single(acquisitions.Created);
    }

    [Fact]
    public async Task CommitThreadsTheLibraryAndProfileChoicesToAcquisitionsAndTheContainerMonitor() {
        var proposal = Container(ProposalKind.Person, "Author", "A1", Leaf(ProposalKind.Book, "Elantris", "W1"));
        var (service, _, acquisitions, monitors) = ServiceWithMonitors(proposal);
        var rootId = Guid.NewGuid();
        var profileId = Guid.NewGuid();

        await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Author, $"{Provider}:A1", [$"{Provider}:W1"], rootId, profileId),
            hideNsfw: false, CancellationToken.None);

        var created = Assert.Single(acquisitions.Created);
        Assert.Equal(rootId, created.TargetLibraryRootId);
        Assert.Equal(profileId, created.ProfileId);

        // The choices stick to the container monitor so later phantom requests inherit them.
        var stored = Assert.Single(monitors.EntityMonitorTargetings);
        Assert.Equal(new AcquisitionTargeting(rootId, profileId), stored);
    }

    [Fact]
    public async Task RequestEntityInheritsTheFollowedContainersChoices() {
        var proposal = Leaf(ProposalKind.Book, "New Work", "W9");
        var (service, writer, acquisitions, monitors) = ServiceWithMonitors(proposal);
        var parentId = Guid.NewGuid();
        var phantomId = Guid.NewGuid();
        writer.Container = new MonitorableContainer(
            phantomId, EntityKind.Book, "New Work", [new ProviderRef(Provider, "W9")], ParentEntityId: parentId);
        monitors.StoredTargeting = new AcquisitionTargeting(Guid.NewGuid(), Guid.NewGuid());

        var response = await service.RequestEntityAsync(phantomId, hideNsfw: true, CancellationToken.None);

        Assert.NotNull(response);
        var created = Assert.Single(acquisitions.Created);
        Assert.Equal(monitors.StoredTargeting.TargetLibraryRootId, created.TargetLibraryRootId);
        Assert.Equal(monitors.StoredTargeting.ProfileId, created.ProfileId);
    }

    [Fact]
    public async Task ContainerSyncNeverClobbersTheMonitorsStoredChoices() {
        var proposal = Container(ProposalKind.Person, "Author", "A1", Leaf(ProposalKind.Book, "Brand New", "W2"));
        var (service, writer, _, monitors) = ServiceWithMonitors(proposal);
        var authorEntityId = FakeWantedEntityWriter.EntityIdFor("A1");
        writer.Container = new MonitorableContainer(
            authorEntityId, EntityKind.BookAuthor, "Author", [new ProviderRef(Provider, "A1")]);

        await service.SyncContainerAsync(authorEntityId, CancellationToken.None);

        // A sync passes no targeting, so the store keeps whatever an explicit request stored earlier.
        var stored = Assert.Single(monitors.EntityMonitorTargetings);
        Assert.Null(stored);
    }

    [Fact]
    public async Task RequestEntityRefusesContainersAndUnknownEntities() {
        var (service, writer, _, _) = ServiceWithMonitors(Leaf(ProposalKind.Book, "Book", "W1"));

        Assert.Null(await service.RequestEntityAsync(Guid.NewGuid(), hideNsfw: true, CancellationToken.None));

        // Containers are monitored/synced, not leaf-requested.
        var authorId = Guid.NewGuid();
        writer.Container = new MonitorableContainer(authorId, EntityKind.BookAuthor, "Author", [new ProviderRef(Provider, "A1")]);
        Assert.Null(await service.RequestEntityAsync(authorId, hideNsfw: true, CancellationToken.None));
    }

    [Fact]
    public async Task ContainerSyncFailsCleanlyWhenTheEntityOrProviderIsGone() {
        var (service, writer, _, _) = ServiceWithMonitors(Container(ProposalKind.Person, "Author", "A1"));

        // Entity gone entirely.
        Assert.False(await service.SyncContainerAsync(Guid.NewGuid(), CancellationToken.None));

        // Entity exists but carries no provider identity to re-resolve from.
        var entityId = Guid.NewGuid();
        writer.Container = new MonitorableContainer(entityId, EntityKind.BookAuthor, "Author", []);
        Assert.False(await service.SyncContainerAsync(entityId, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveWantedSuppressesDeletesAndTearsDownAcquisitions() {
        var (service, writer, acquisitions, _, suppressions) = ServiceWithSuppressions(Leaf(ProposalKind.Book, "Book", "W1"));
        var phantomId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        writer.Container = new MonitorableContainer(
            phantomId, EntityKind.Book, "The Martian", [new ProviderRef(Provider, "W1"), new ProviderRef("isbn13", "978")]);
        acquisitions.AcquisitionIdsByEntity[phantomId] = [acquisitionId];

        var removed = await service.RemoveWantedAsync([phantomId], CancellationToken.None);

        Assert.Equal(1, removed);
        // Every identity the entity carried is blacklisted, its download torn down, the entity deleted.
        Assert.Contains($"{Provider}:W1", suppressions.Suppressed);
        Assert.Contains("isbn13:978", suppressions.Suppressed);
        Assert.Equal([acquisitionId], acquisitions.Deleted.ToArray());
    }

    [Fact]
    public async Task RemoveWantedSkipsOnDiskEntities() {
        var (service, writer, acquisitions, _, suppressions) = ServiceWithSuppressions(Leaf(ProposalKind.Book, "Book", "W1"));
        var ownedId = Guid.NewGuid();
        writer.Container = new MonitorableContainer(
            ownedId, EntityKind.Book, "Owned", [new ProviderRef(Provider, "W1")], HasSourceFile: true);

        Assert.Equal(0, await service.RemoveWantedAsync([ownedId], CancellationToken.None));
        Assert.Empty(suppressions.Suppressed);
        Assert.Empty(acquisitions.Deleted);
    }

    [Fact]
    public async Task ContainerSyncNeverResurrectsASuppressedWork() {
        var proposal = Container(ProposalKind.Person, "Author", "A1",
            Leaf(ProposalKind.Book, "Removed", "W1"), Leaf(ProposalKind.Book, "Kept", "W2"));
        var (service, writer, _, _, suppressions) = ServiceWithSuppressions(proposal);
        var authorEntityId = FakeWantedEntityWriter.EntityIdFor("A1");
        writer.Container = new MonitorableContainer(authorEntityId, EntityKind.BookAuthor, "Author", [new ProviderRef(Provider, "A1")]);
        suppressions.Suppressed.Add($"{Provider}:W1"); // the user removed this work earlier

        Assert.True(await service.SyncContainerAsync(authorEntityId, CancellationToken.None));

        Assert.DoesNotContain(writer.Ensured, call => call.ItemId == "W1");
        Assert.Contains(writer.Ensured, call => call.ItemId == "W2");
    }

    [Fact]
    public async Task ExplicitRequestClearsTheSuppression() {
        var proposal = Leaf(ProposalKind.Book, "The Martian", "W1");
        var (service, _, acquisitions, _, suppressions) = ServiceWithSuppressions(proposal);
        suppressions.Suppressed.Add($"{Provider}:W1");

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Book, $"{Provider}:W1", []), hideNsfw: false, CancellationToken.None);

        Assert.Equal(RequestCommitOutcome.Requested, Assert.Single(response!.Items).Outcome);
        Assert.Contains($"{Provider}:W1", suppressions.Cleared);
        Assert.Single(acquisitions.Created);
    }

    [Fact]
    public async Task MalformedOrUnresolvableExternalIdsReturnNull() {
        var (service, _, _) = Service(Leaf(ProposalKind.Book, "Book", "W1"));

        Assert.Null(await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Book, "missing-separator", []), hideNsfw: false, CancellationToken.None));
        Assert.Null(await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Book, $"{Provider}:W404", []), hideNsfw: false, CancellationToken.None));
    }

    private static (RequestCommitService Service, FakeWantedEntityWriter Writer, FakeAcquisitionRequestService Acquisitions, FakeMonitorStore Monitors) ServiceWithMonitors(
        EntityMetadataProposal proposal) {
        var (service, writer, acquisitions, monitors, _) = ServiceWithSuppressions(proposal);
        return (service, writer, acquisitions, monitors);
    }

    private static (RequestCommitService Service, FakeWantedEntityWriter Writer, FakeAcquisitionRequestService Acquisitions, FakeMonitorStore Monitors, FakeSuppressionStore Suppressions) ServiceWithSuppressions(
        EntityMetadataProposal proposal) {
        var writer = new FakeWantedEntityWriter();
        var acquisitions = new FakeAcquisitionRequestService();
        var monitors = new FakeMonitorStore();
        var suppressions = new FakeSuppressionStore();
        var service = new RequestCommitService(new FakeProposalSource(proposal), writer, acquisitions, monitors, suppressions);
        return (service, writer, acquisitions, monitors, suppressions);
    }

    private static (RequestCommitService Service, FakeWantedEntityWriter Writer, FakeAcquisitionRequestService Acquisitions) Service(
        EntityMetadataProposal proposal) {
        var (service, writer, acquisitions, _) = ServiceWithMonitors(proposal);
        return (service, writer, acquisitions);
    }

    private static EntityMetadataProposal Container(ProposalKind kind, string title, string itemId, params EntityMetadataProposal[] works) =>
        new($"p-{itemId}", Provider, kind, null, null, Patch(title, itemId), [], works, [], null, []);

    private static EntityMetadataProposal Leaf(ProposalKind kind, string title, string workId) =>
        new($"p-{workId}", Provider, kind, null, null, Patch(title, workId), [], [], [], null, []);

    private static EntityMetadataPatch Patch(string title, string workId, IReadOnlyDictionary<string, int>? positions = null) =>
        new(title, null, new Dictionary<string, string> { [Provider] = workId }, [], [], null, [],
            new Dictionary<string, string>(), new Dictionary<string, int>(), positions ?? new Dictionary<string, int>(), null);

    /// <summary>Resolves any node of the proposal tree by its provider item id — the shape of a plugin's per-item lookups.</summary>
    private sealed class FakeProposalSource(EntityMetadataProposal proposal) : IPluginRequestProposalSource {
        public Task<EntityMetadataProposal?> ResolveProposalAsync(
            RequestKindDescriptor descriptor, string providerId, string itemId, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) =>
            Task.FromResult(FindByItemId(proposal, providerId, itemId));

        private static EntityMetadataProposal? FindByItemId(EntityMetadataProposal node, string providerId, string itemId) {
            if (node.Patch?.ExternalIds.GetValueOrDefault(providerId) == itemId) {
                return node;
            }

            foreach (var child in node.Children) {
                if (FindByItemId(child, providerId, itemId) is { } found) {
                    return found;
                }
            }

            return null;
        }
    }

    private sealed class FakeWantedEntityWriter : IWantedEntityWriter {
        public sealed record EnsureCall(EntityKind Kind, string ItemId, string Title, Guid? ParentEntityId, bool MatchTitleKindWide);
        public sealed record ApplyCall(Guid EntityId, EntityMetadataProposal Proposal);

        public List<EnsureCall> Ensured { get; } = [];
        public List<ApplyCall> Applied { get; } = [];

        /// <summary>Item ids that resolve to an existing entity owning a real file.</summary>
        public HashSet<string> ExistingWithFile { get; } = [];

        /// <summary>Item ids that resolve to an existing (fileless) wanted entity.</summary>
        public HashSet<string> ExistingWanted { get; } = [];

        /// <summary>Deterministic entity id per provider item id, so tests can pre-wire acquisition state.</summary>
        public static Guid EntityIdFor(string itemId) =>
            new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(itemId)));

        public Task<WantedEntityResult> EnsureAsync(EntityKind kind, string providerId, string itemId, string title, Guid? parentEntityId, bool matchTitleKindWide, CancellationToken cancellationToken) {
            Ensured.Add(new EnsureCall(kind, itemId, title, parentEntityId, matchTitleKindWide));
            var hasFile = ExistingWithFile.Contains(itemId);
            var created = !hasFile && !ExistingWanted.Contains(itemId);
            return Task.FromResult(new WantedEntityResult(EntityIdFor(itemId), created, hasFile));
        }

        public Task ApplyProposalAsync(Guid entityId, EntityMetadataProposal proposal, CancellationToken cancellationToken) {
            Applied.Add(new ApplyCall(entityId, proposal));
            return Task.CompletedTask;
        }

        public Task<bool> DeleteIfWantedAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        /// <summary>Container ref returned by GetContainerAsync, for sync tests.</summary>
        public MonitorableContainer? Container { get; set; }

        public Task<MonitorableContainer?> GetContainerAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(Container?.EntityId == entityId ? Container : null);
    }

    private sealed class FakeMonitorStore : Prismedia.Application.Acquisition.IMonitorStore {
        public List<Guid> AcquisitionMonitors { get; } = [];
        public List<Guid> EntityMonitors { get; } = [];
        public List<AcquisitionTargeting?> EntityMonitorTargetings { get; } = [];
        public List<MonitorPreset?> EntityMonitorPresets { get; } = [];
        public AcquisitionTargeting? StoredTargeting { get; set; }

        /// <summary>Preset returned by GetPresetByEntityAsync, for sync-gating tests. Null models a monitor with no stored preset.</summary>
        public MonitorPreset? StoredPreset { get; set; }

        public Task<MonitorView> StartAsync(Guid acquisitionId, EntityKind kind, string title, string? author, CancellationToken cancellationToken) {
            AcquisitionMonitors.Add(acquisitionId);
            return Task.FromResult(View(kind, title, acquisitionId: acquisitionId));
        }

        public Task<MonitorView> StartForEntityAsync(Guid entityId, EntityKind kind, string title, AcquisitionTargeting? targeting, MonitorPreset? preset, CancellationToken cancellationToken) {
            EntityMonitors.Add(entityId);
            EntityMonitorTargetings.Add(targeting);
            EntityMonitorPresets.Add(preset);
            return Task.FromResult(View(kind, title, entityId: entityId));
        }

        private static MonitorView View(EntityKind kind, string title, Guid? acquisitionId = null, Guid? entityId = null) =>
            new(Guid.NewGuid(), kind, acquisitionId, MonitorStatus.Active, title, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, entityId);

        public Task<MonitorView?> GetByEntityAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult<MonitorView?>(null);
        public Task<AcquisitionTargeting?> GetTargetingByEntityAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult(StoredTargeting);
        public Task<MonitorPreset?> GetPresetByEntityAsync(Guid entityId, CancellationToken cancellationToken) => Task.FromResult(StoredPreset);
        public Task<bool> DeleteAsync(Guid monitorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetStatusAsync(Guid monitorId, MonitorStatus status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MonitorView>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Prismedia.Application.Acquisition.WantedPage> ListMissingAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Prismedia.Application.Acquisition.WantedPage> ListCutoffUnmetAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView?> GetByAcquisitionAsync(Guid acquisitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Prismedia.Application.Acquisition.DueMonitor>> ListDueMonitorsAsync(int defaultIntervalMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkSearchedAsync(Guid monitorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> CreateUpgradeChildAsync(Guid monitorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResolveUpgradeChildAsync(Guid childId, bool succeeded, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasActiveMonitorsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAcquisitionRequestService : IAcquisitionRequestService {
        public List<AcquisitionCreateRequest> Created { get; } = [];
        public HashSet<Guid> EntitiesWithAcquisitions { get; } = [];
        public Dictionary<Guid, Guid[]> AcquisitionIdsByEntity { get; } = [];
        public List<Guid> Deleted { get; } = [];

        public Task<AcquisitionSummary> CreateAndSearchAsync(AcquisitionCreateRequest request, CancellationToken cancellationToken) {
            Created.Add(request);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new AcquisitionSummary(
                Guid.NewGuid(), AcquisitionStatus.Pending, null, request.Title, request.Author, request.Series,
                request.Year, request.PosterUrl, null, now, now, request.Description, request.Kind, request.EntityId));
        }

        public Task<bool> AnyForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(EntitiesWithAcquisitions.Contains(entityId));

        public Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(AcquisitionIdsByEntity.GetValueOrDefault(entityId) ?? []);

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) {
            Deleted.Add(id);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeSuppressionStore : IWantedSuppressionStore {
        public List<string> Suppressed { get; } = [];
        public List<string> Cleared { get; } = [];

        public Task SuppressAsync(IReadOnlyList<ProviderRef> identities, EntityKind kind, string title, CancellationToken cancellationToken) {
            Suppressed.AddRange(identities.Select(identity => $"{identity.Provider}:{identity.ItemId}"));
            return Task.CompletedTask;
        }

        public Task<IReadOnlySet<string>> FilterSuppressedAsync(IReadOnlyList<ProviderRef> identities, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(identities
                .Select(identity => $"{identity.Provider}:{identity.ItemId}")
                .Where(key => Suppressed.Contains(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase));

        public Task ClearAsync(IReadOnlyList<ProviderRef> identities, CancellationToken cancellationToken) {
            foreach (var identity in identities) {
                var key = $"{identity.Provider}:{identity.ItemId}";
                Cleared.Add(key);
                Suppressed.Remove(key);
            }

            return Task.CompletedTask;
        }
    }
}
