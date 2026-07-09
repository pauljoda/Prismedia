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
    public async Task ChildSelectionPreservesOpaqueIdentityCaseAndColons() {
        var proposal = Container(
            ProposalKind.Person,
            "Author",
            "A1",
            Leaf(ProposalKind.Book, "Exact", "Work:AbC"),
            Leaf(ProposalKind.Book, "Different case", "Work:aBc"));
        var (service, writer, acquisitions) = Service(proposal);

        var response = await service.CommitAsync(
            new RequestCommitRequest(
                RequestMediaKind.Author,
                $"{Provider}:A1",
                [$"{Provider}:Work:AbC"]),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal("Work:AbC", Assert.Single(writer.Ensured, call => call.Kind == EntityKind.Book).ItemId);
        Assert.Equal("Exact", Assert.Single(acquisitions.Created).Title);
        Assert.Equal($"{Provider}:Work:AbC", Assert.Single(response!.Items).ExternalId);
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
    public async Task ExistingSeasonPickStillStartsAnAcquisitionAndMaterializesMissingEpisodes() {
        var episode1 = Leaf(ProposalKind.Video, "Pilot", "E1") with {
            Patch = Patch("Pilot", "E1", new Dictionary<string, int> { ["seasonNumber"] = 1, ["episodeNumber"] = 1 }),
        };
        var episode2 = Leaf(ProposalKind.Video, "Aftermath", "E2") with {
            Patch = Patch("Aftermath", "E2", new Dictionary<string, int> { ["seasonNumber"] = 1, ["episodeNumber"] = 2 }),
        };
        var season = Container(ProposalKind.VideoSeason, "Season 1", "S1", episode1, episode2) with {
            Patch = Patch("Season 1", "S1", new Dictionary<string, int> { ["seasonNumber"] = 1 }),
        };
        var (service, writer, acquisitions, monitors) = ServiceWithMonitors(
            Container(ProposalKind.VideoSeries, "Andor", "TV1", season));
        writer.ExistingWithFile.Add("S1"); // existing series/season folder with missing children
        writer.ExistingWithFile.Add("E1"); // episode already present; E2 is missing

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Series, $"{Provider}:TV1", [$"{Provider}:S1"], Preset: MonitorPreset.All),
            hideNsfw: false, CancellationToken.None);

        Assert.NotNull(response);
        var created = Assert.Single(acquisitions.Created);
        Assert.Equal(EntityKind.VideoSeason, created.Kind);
        Assert.Equal(FakeWantedEntityWriter.EntityIdFor("S1"), created.EntityId);
        Assert.Equal("Andor", created.Series);
        Assert.Equal(1, created.SeasonNumber);
        Assert.Single(monitors.AcquisitionMonitors);

        Assert.Contains(writer.Ensured, call => call.Kind == EntityKind.Video && call.ItemId == "E2");
        Assert.Contains(writer.Applied, call =>
            call.EntityId == FakeWantedEntityWriter.EntityIdFor("S1") &&
            call.Proposal.Children.Any(child => child.Patch?.ExternalIds.GetValueOrDefault(Provider) == "E2") &&
            !call.Proposal.Children.Any(child => child.Patch?.ExternalIds.GetValueOrDefault(Provider) == "E1"));
    }

    [Fact]
    public async Task RequestingExistingOwnedSeasonStartsSeasonMonitorAndMaterializesMissingEpisodes() {
        var episode1 = Leaf(ProposalKind.Video, "Pilot", "E1") with {
            Patch = Patch("Pilot", "E1", new Dictionary<string, int> { ["seasonNumber"] = 1, ["episodeNumber"] = 1 }),
        };
        var episode2 = Leaf(ProposalKind.Video, "Aftermath", "E2") with {
            Patch = Patch("Aftermath", "E2", new Dictionary<string, int> { ["seasonNumber"] = 1, ["episodeNumber"] = 2 }),
        };
        var season = Container(ProposalKind.VideoSeason, "Season 1", "S1", episode1, episode2) with {
            Patch = Patch("Season 1", "S1", new Dictionary<string, int> { ["seasonNumber"] = 1 }),
        };
        var (service, writer, acquisitions, monitors) = ServiceWithMonitors(
            Container(ProposalKind.VideoSeries, "Andor", "TV1", season));
        var seriesId = FakeWantedEntityWriter.EntityIdFor("TV1");
        var seasonId = FakeWantedEntityWriter.EntityIdFor("S1");
        writer.Containers[seriesId] = new MonitorableContainer(seriesId, EntityKind.VideoSeries, "Andor", [new ExternalIdentity(Provider, "TV1")]);
        writer.Containers[seasonId] = new MonitorableContainer(
            seasonId, EntityKind.VideoSeason, "Season 1", [new ExternalIdentity(Provider, "S1")],
            HasSourceFile: true, ParentEntityId: seriesId,
            Positions: new Dictionary<string, int> { [EntityPositionCodes.Season] = 1 });
        writer.ExistingWithFile.Add("E1");

        var response = await service.RequestEntityAsync(seasonId, hideNsfw: false, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(RequestCommitOutcome.Requested, Assert.Single(response!.Items).Outcome);
        var created = Assert.Single(acquisitions.Created);
        Assert.Equal(EntityKind.VideoSeason, created.Kind);
        Assert.Equal(seasonId, created.EntityId);
        Assert.Equal("Andor", created.Series);
        Assert.Equal(1, created.SeasonNumber);
        Assert.Single(monitors.AcquisitionMonitors);
        Assert.Contains(writer.Ensured, call => call.Kind == EntityKind.Video && call.ItemId == "E2" && call.ParentEntityId == seasonId);
    }

    [Fact]
    public async Task RequestMissingChildrenRequestsEachWantedEpisodeIndividually() {
        // A season pack imported with gaps: episodes 2 and 3 remain wanted phantoms under the season.
        // The fallback requests each as its own monitored episode acquisition from the entity graph.
        var (service, writer, acquisitions, monitors) = ServiceWithMonitors(Container(ProposalKind.VideoSeries, "Andor", "TV1"));
        var seriesId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var episode2 = Guid.NewGuid();
        var episode3 = Guid.NewGuid();
        writer.Containers[seriesId] = new MonitorableContainer(seriesId, EntityKind.VideoSeries, "Andor", []);
        writer.Containers[seasonId] = new MonitorableContainer(
            seasonId, EntityKind.VideoSeason, "Season 1", [new ExternalIdentity(Provider, "S1")],
            HasSourceFile: true, ParentEntityId: seriesId,
            Positions: new Dictionary<string, int> { [EntityPositionCodes.Season] = 1 });
        writer.Containers[episode2] = new MonitorableContainer(
            episode2, EntityKind.Video, "Aftermath", [], ParentEntityId: seasonId,
            Positions: new Dictionary<string, int> { [EntityPositionCodes.Season] = 1, [EntityPositionCodes.Episode] = 2 });
        writer.Containers[episode3] = new MonitorableContainer(
            episode3, EntityKind.Video, "Reckoning", [], ParentEntityId: seasonId,
            Positions: new Dictionary<string, int> { [EntityPositionCodes.Season] = 1, [EntityPositionCodes.Episode] = 3 });
        writer.WantedChildren[seasonId] = [episode2, episode3];

        var (covered, missing) = await service.RequestMissingChildrenAsync(seasonId, CancellationToken.None);

        Assert.Equal(2, missing);
        Assert.Equal(2, covered);
        Assert.Equal(2, acquisitions.Created.Count);
        Assert.All(acquisitions.Created, request => Assert.Equal(EntityKind.Video, request.Kind));
        Assert.All(acquisitions.Created, request => Assert.Equal("Andor", request.Series));
        Assert.All(acquisitions.Created, request => Assert.Equal(1, request.SeasonNumber));
        Assert.Equal([2, 3], acquisitions.Created.Select(request => request.EpisodeNumber).ToArray());
        Assert.Equal(2, monitors.AcquisitionMonitors.Count);
    }

    [Fact]
    public async Task RequestingAWantedSeriesRequestsItsWantedSeasons() {
        // A container phantom has no acquirable unit of its own: its "Search for release" requests
        // each still-wanted child instead (the series' unrequested seasons as season packs).
        var (service, writer, acquisitions, _) = ServiceWithMonitors(Container(ProposalKind.VideoSeries, "Andor", "TV1"));
        var seriesId = Guid.NewGuid();
        var season1 = Guid.NewGuid();
        var season2 = Guid.NewGuid();
        writer.Containers[seriesId] = new MonitorableContainer(seriesId, EntityKind.VideoSeries, "Andor", [new ExternalIdentity(Provider, "TV1")]);
        writer.Containers[season1] = new MonitorableContainer(
            season1, EntityKind.VideoSeason, "Season 1", [], ParentEntityId: seriesId,
            Positions: new Dictionary<string, int> { [EntityPositionCodes.Season] = 1 });
        writer.Containers[season2] = new MonitorableContainer(
            season2, EntityKind.VideoSeason, "Season 2", [], ParentEntityId: seriesId,
            Positions: new Dictionary<string, int> { [EntityPositionCodes.Season] = 2 });
        writer.WantedChildren[seriesId] = [season1, season2];

        var response = await service.RequestEntityAsync(seriesId, hideNsfw: false, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(seriesId, response!.ContainerEntityId);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal(2, acquisitions.Created.Count);
        Assert.All(acquisitions.Created, request => Assert.Equal(EntityKind.VideoSeason, request.Kind));
        Assert.All(acquisitions.Created, request => Assert.Equal("Andor", request.Series));
        Assert.Equal([1, 2], acquisitions.Created.Select(request => request.SeasonNumber).ToArray());
    }

    [Fact]
    public async Task RequestMissingChildrenCountsAnInFlightEpisodeAsCoveredWithoutDuplicating() {
        var (service, writer, acquisitions, _) = ServiceWithMonitors(Container(ProposalKind.VideoSeries, "Andor", "TV1"));
        var seasonId = Guid.NewGuid();
        var episode2 = Guid.NewGuid();
        var episode3 = Guid.NewGuid();
        writer.Containers[seasonId] = new MonitorableContainer(
            seasonId, EntityKind.VideoSeason, "Season 1", [new ExternalIdentity(Provider, "S1")],
            HasSourceFile: true,
            Positions: new Dictionary<string, int> { [EntityPositionCodes.Season] = 1 });
        writer.Containers[episode2] = new MonitorableContainer(
            episode2, EntityKind.Video, "Aftermath", [], ParentEntityId: seasonId,
            Positions: new Dictionary<string, int> { [EntityPositionCodes.Season] = 1, [EntityPositionCodes.Episode] = 2 });
        writer.Containers[episode3] = new MonitorableContainer(
            episode3, EntityKind.Video, "Reckoning", [], ParentEntityId: seasonId,
            Positions: new Dictionary<string, int> { [EntityPositionCodes.Season] = 1, [EntityPositionCodes.Episode] = 3 });
        writer.WantedChildren[seasonId] = [episode2, episode3];
        acquisitions.EntitiesWithAcquisitions.Add(episode2); // already chasing this gap

        var (covered, missing) = await service.RequestMissingChildrenAsync(seasonId, CancellationToken.None);

        Assert.Equal(2, missing);
        Assert.Equal(2, covered); // the in-flight episode counts as covered, not duplicated
        Assert.Equal(episode3, Assert.Single(acquisitions.Created).EntityId);
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
            authorEntityId, EntityKind.BookAuthor, "Author", [new ExternalIdentity(Provider, "A1")]);
        writer.ExistingWanted.Add("W1"); // already tracked from an earlier request
        acquisitions.EntitiesWithAcquisitions.Add(FakeWantedEntityWriter.EntityIdFor("W1"));

        var synced = await service.SyncContainerAsync(authorEntityId, CancellationToken.None);

        Assert.True(synced);
        // Discovery materializes the missing work as a wanted phantom but never downloads on its own.
        Assert.Contains(writer.Ensured, call => call.ItemId == "W2");
        Assert.Empty(acquisitions.Created);
        Assert.Empty(monitors.AcquisitionMonitors);
    }

    [Fact]
    public async Task ContainerSyncUsesPersistentIdentityNamespaceIndependentlyFromProposalPluginId() {
        var proposal = Rekey(
            Container(ProposalKind.Person, "Author", "A1", Leaf(ProposalKind.Book, "Brand New", "W2")),
            identityNamespace: "tmdb",
            pluginId: "metadata-aggregator");
        var (service, writer, acquisitions, _) = ServiceWithMonitors(proposal);
        var authorEntityId = FakeWantedEntityWriter.EntityIdFor("A1");
        writer.Container = new MonitorableContainer(
            authorEntityId,
            EntityKind.BookAuthor,
            "Author",
            [new ExternalIdentity("tmdb", "A1")]);

        var synced = await service.SyncContainerAsync(authorEntityId, CancellationToken.None);

        Assert.True(synced);
        Assert.Contains(writer.Ensured, call => call.ItemId == "W2");
        Assert.Empty(acquisitions.Created);
    }

    [Theory]
    [InlineData(MonitorPreset.All)]
    [InlineData(MonitorPreset.Future)]
    public async Task ContainerSyncWithAnAutoMonitorPresetMaterializesNewWorks(MonitorPreset preset) {
        var proposal = Container(ProposalKind.Person, "Author", "A1", Leaf(ProposalKind.Book, "Brand New", "W2"));
        var (service, writer, acquisitions, monitors) = ServiceWithMonitors(proposal);
        var authorEntityId = FakeWantedEntityWriter.EntityIdFor("A1");
        writer.Container = new MonitorableContainer(authorEntityId, EntityKind.BookAuthor, "Author", [new ExternalIdentity(Provider, "A1")]);
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
        writer.Container = new MonitorableContainer(authorEntityId, EntityKind.BookAuthor, "Author", [new ExternalIdentity(Provider, "A1")]);
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
        writer.Container = new MonitorableContainer(authorEntityId, EntityKind.BookAuthor, "Author", [new ExternalIdentity(Provider, "A1")]);
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
            phantomId, EntityKind.Book, "The Martian", [new ExternalIdentity("isbn13", "9780000000000"), new ExternalIdentity(Provider, "W1")]);

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
            phantomId, EntityKind.Book, "New Work", [new ExternalIdentity(Provider, "W9")], ParentEntityId: parentId);
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
            authorEntityId, EntityKind.BookAuthor, "Author", [new ExternalIdentity(Provider, "A1")]);

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
        writer.Container = new MonitorableContainer(authorId, EntityKind.BookAuthor, "Author", [new ExternalIdentity(Provider, "A1")]);
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
            phantomId, EntityKind.Book, "The Martian", [new ExternalIdentity(Provider, "W1"), new ExternalIdentity("isbn13", "978")]);
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
            ownedId, EntityKind.Book, "Owned", [new ExternalIdentity(Provider, "W1")], HasSourceFile: true);

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
        writer.Container = new MonitorableContainer(authorEntityId, EntityKind.BookAuthor, "Author", [new ExternalIdentity(Provider, "A1")]);
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
    public async Task ExplicitRequestClearsOnlyCanonicalPersistentIdentities() {
        var proposal = Leaf(ProposalKind.Book, "The Martian", "W1") with {
            Patch = Patch("The Martian", "W1") with {
                ExternalIds = new Dictionary<string, string> {
                    [Provider] = "W1",
                    [" TMDB "] = " 603 ",
                    ["transient_locator"] = "https://example.test/items/603"
                }
            }
        };
        var (service, _, _, _, suppressions) = ServiceWithSuppressions(proposal);

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Book, $"{Provider}:W1", []),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal(RequestCommitOutcome.Requested, Assert.Single(response!.Items).Outcome);
        Assert.Equal([$"{Provider}:W1", "tmdb:603"], suppressions.Cleared);
    }

    [Fact]
    public async Task ReviewedCommitRejectsRevisionDriftBeforeAnyWrite() {
        var identity = new ExternalIdentity("tmdb", "603");
        var proposal = Node("movie:603", "cinema-metadata", ProposalKind.Movie, "The Matrix", identity);
        var current = Review(
            "cinema-metadata",
            RequestMediaKind.Movie,
            identity,
            proposal,
            [Target(proposal, RequestMediaKind.Movie, identity)]);
        var reviews = new FakeReviewSource(_ => current);
        var (service, writer, acquisitions, monitors, suppressions) = ReviewedService(proposal, reviews);

        await Assert.ThrowsAsync<RequestProposalChangedException>(() => service.CommitReviewedAsync(
            new ReviewedRequestCommitRequest(
                RequestMediaKind.Movie,
                "cinema-metadata",
                identity,
                ProposalRevision: new string('0', 64),
                SelectedProposalIds: [proposal.ProposalId]),
            hideNsfw: false,
            CancellationToken.None));

        Assert.Single(reviews.RevalidateCalls);
        Assert.Empty(writer.Ensured);
        Assert.Empty(writer.Applied);
        Assert.Empty(acquisitions.Created);
        Assert.Empty(monitors.EntityMonitors);
        Assert.Empty(monitors.AcquisitionMonitors);
        Assert.Empty(suppressions.Cleared);
    }

    [Fact]
    public async Task ReviewedMovieCommitsOnlyTheReviewedRootProposal() {
        var identity = new ExternalIdentity("tmdb", "Movie:603");
        var proposal = Node("movie:603", "cinema-metadata", ProposalKind.Movie, "The Matrix", identity);
        var review = Review(
            "cinema-metadata",
            RequestMediaKind.Movie,
            identity,
            proposal,
            [Target(proposal, RequestMediaKind.Movie, identity)]);
        var reviews = new FakeReviewSource(_ => review);
        var (service, writer, acquisitions, _, _) = ReviewedService(proposal, reviews);

        var response = await service.CommitReviewedAsync(
            new ReviewedRequestCommitRequest(
                RequestMediaKind.Movie,
                "cinema-metadata",
                identity,
                review.Revision,
                [proposal.ProposalId]),
            hideNsfw: false,
            CancellationToken.None);

        var wantedMovie = Assert.Single(writer.Ensured);
        Assert.Equal(EntityKind.Movie, wantedMovie.Kind);
        Assert.Equal("tmdb", wantedMovie.IdentityNamespace);
        Assert.Equal("Movie:603", wantedMovie.ItemId);
        Assert.Equal("tmdb", Assert.Single(acquisitions.Created).IdentityNamespace);
        Assert.Equal("tmdb:Movie:603", Assert.Single(response!.Items).ExternalId);
    }

    [Fact]
    public async Task ReviewedAuthorMapsOpaqueProposalIdToServerDerivedChildIdentity() {
        var rootIdentity = new ExternalIdentity("authors", "Author:A");
        var childIdentity = new ExternalIdentity("works", "Work:AbC");
        var unselectedIdentity = new ExternalIdentity("works", "Work:aBc");
        var selected = Node("book:Work:AbC", "books-metadata", ProposalKind.Book, "Exact", childIdentity);
        var unselected = Node("book:Work:aBc", "books-metadata", ProposalKind.Book, "Different case", unselectedIdentity);
        var proposal = Node(
            "author:Author:A",
            "books-metadata",
            ProposalKind.Person,
            "Author",
            rootIdentity,
            selected,
            unselected);
        var review = Review(
            "books-metadata",
            RequestMediaKind.Author,
            rootIdentity,
            proposal,
            [
                Target(proposal, RequestMediaKind.Author, rootIdentity),
                Target(selected, RequestMediaKind.Book, childIdentity),
                Target(unselected, RequestMediaKind.Book, unselectedIdentity)
            ]);
        var reviews = new FakeReviewSource(_ => review);
        var (service, writer, acquisitions, _, _) = ReviewedService(proposal, reviews);

        var response = await service.CommitReviewedAsync(
            new ReviewedRequestCommitRequest(
                RequestMediaKind.Author,
                "books-metadata",
                rootIdentity,
                review.Revision,
                ["book:Work:AbC"]),
            hideNsfw: false,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains(writer.Ensured, call =>
            call.Kind == EntityKind.BookAuthor
            && call.IdentityNamespace == "authors"
            && call.ItemId == "Author:A");
        var book = Assert.Single(writer.Ensured, call => call.Kind == EntityKind.Book);
        Assert.Equal("works", book.IdentityNamespace);
        Assert.Equal("Work:AbC", book.ItemId);
        var acquisition = Assert.Single(acquisitions.Created);
        Assert.Equal("works", acquisition.IdentityNamespace);
        Assert.Equal("Work:AbC", acquisition.IdentityValue);
        Assert.Equal("works:Work:AbC", Assert.Single(response!.Items).ExternalId);
    }

    [Theory]
    [InlineData("episode:one")]
    [InlineData("season:missing")]
    [InlineData("season:ONE")]
    public async Task ReviewedSeriesRejectsNestedUnknownAndWrongCaseSelections(string selectedProposalId) {
        var rootIdentity = new ExternalIdentity("tmdb", "series:1");
        var seasonIdentity = new ExternalIdentity("tvdb", "season:one");
        var episodeIdentity = new ExternalIdentity("episode-db", "episode:one");
        var episode = Node("episode:one", "series-metadata", ProposalKind.Video, "Pilot", episodeIdentity);
        var season = Node("season:one", "series-metadata", ProposalKind.VideoSeason, "Season 1", seasonIdentity, episode);
        var proposal = Node("series:one", "series-metadata", ProposalKind.VideoSeries, "Series", rootIdentity, season);
        var review = Review(
            "series-metadata",
            RequestMediaKind.Series,
            rootIdentity,
            proposal,
            [
                Target(proposal, RequestMediaKind.Series, rootIdentity),
                Target(season, RequestMediaKind.Season, seasonIdentity),
                Target(episode, RequestMediaKind.Episode, episodeIdentity)
            ]);
        var reviews = new FakeReviewSource(_ => review);
        var (service, writer, acquisitions, _, _) = ReviewedService(proposal, reviews);

        await Assert.ThrowsAsync<RequestCommitValidationException>(() => service.CommitReviewedAsync(
            new ReviewedRequestCommitRequest(
                RequestMediaKind.Series,
                "series-metadata",
                rootIdentity,
                review.Revision,
                [selectedProposalId]),
            hideNsfw: false,
            CancellationToken.None));

        Assert.Empty(writer.Ensured);
        Assert.Empty(acquisitions.Created);
    }

    [Fact]
    public async Task ReviewedCommitRejectsDuplicateSelectionBeforeRevalidation() {
        var identity = new ExternalIdentity("tmdb", "603");
        var proposal = Node("movie:603", "cinema-metadata", ProposalKind.Movie, "The Matrix", identity);
        var reviews = new FakeReviewSource(_ => throw new InvalidOperationException("Must not revalidate invalid input."));
        var (service, writer, acquisitions, _, _) = ReviewedService(proposal, reviews);

        await Assert.ThrowsAsync<RequestCommitValidationException>(() => service.CommitReviewedAsync(
            new ReviewedRequestCommitRequest(
                RequestMediaKind.Movie,
                "cinema-metadata",
                identity,
                new string('0', 64),
                [proposal.ProposalId, proposal.ProposalId]),
            hideNsfw: false,
            CancellationToken.None));

        Assert.Empty(reviews.RevalidateCalls);
        Assert.Empty(writer.Ensured);
        Assert.Empty(acquisitions.Created);
    }

    [Fact]
    public async Task ReviewedCommitRejectsSameKindTargetsWithDuplicateExternalIdentityBeforeWrites() {
        var rootIdentity = new ExternalIdentity("authors", "A1");
        var sharedIdentity = new ExternalIdentity("works", "W1");
        var first = Node("book:first", "books-metadata", ProposalKind.Book, "First", sharedIdentity);
        var second = Node("book:second", "books-metadata", ProposalKind.Book, "Second", sharedIdentity);
        var proposal = Node("author:one", "books-metadata", ProposalKind.Person, "Author", rootIdentity, first, second);
        var review = Review(
            "books-metadata",
            RequestMediaKind.Author,
            rootIdentity,
            proposal,
            [
                Target(proposal, RequestMediaKind.Author, rootIdentity),
                Target(first, RequestMediaKind.Book, sharedIdentity),
                Target(second, RequestMediaKind.Book, sharedIdentity)
            ]);
        var reviews = new FakeReviewSource(_ => review);
        var (service, writer, acquisitions, monitors, _) = ReviewedService(proposal, reviews);

        await Assert.ThrowsAsync<RequestCommitValidationException>(() => service.CommitReviewedAsync(
            new ReviewedRequestCommitRequest(
                RequestMediaKind.Author,
                "books-metadata",
                rootIdentity,
                review.Revision,
                [first.ProposalId]),
            hideNsfw: false,
            CancellationToken.None));

        Assert.Empty(writer.Ensured);
        Assert.Empty(acquisitions.Created);
        Assert.Empty(monitors.EntityMonitors);
    }

    [Fact]
    public async Task ReviewedCommitAllowsSameExternalIdentityAcrossDifferentEntityKinds() {
        var sharedIdentity = new ExternalIdentity("shared", "Item:1");
        var book = Node("book:one", "books-metadata", ProposalKind.Book, "Book", sharedIdentity);
        var proposal = Node("author:one", "books-metadata", ProposalKind.Person, "Author", sharedIdentity, book);
        var review = Review(
            "books-metadata",
            RequestMediaKind.Author,
            sharedIdentity,
            proposal,
            [
                Target(proposal, RequestMediaKind.Author, sharedIdentity),
                Target(book, RequestMediaKind.Book, sharedIdentity)
            ]);
        var reviews = new FakeReviewSource(_ => review);
        var (service, writer, acquisitions, _, _) = ReviewedService(proposal, reviews);

        var response = await service.CommitReviewedAsync(
            new ReviewedRequestCommitRequest(
                RequestMediaKind.Author,
                "books-metadata",
                sharedIdentity,
                review.Revision,
                [book.ProposalId]),
            hideNsfw: false,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains(writer.Ensured, call => call.Kind == EntityKind.BookAuthor);
        Assert.Contains(writer.Ensured, call => call.Kind == EntityKind.Book);
        Assert.Single(acquisitions.Created);
    }

    [Fact]
    public async Task ReviewedAuthorDoesNotNestSiblingVolumesUnderSelectedBook() {
        var rootIdentity = new ExternalIdentity("authors", "A1");
        var bookIdentity = new ExternalIdentity("works", "W1");
        var volumeIdentity = new ExternalIdentity("works", "V2");
        var volume = Node("volume:2", "books-metadata", ProposalKind.Book, "Volume Two", volumeIdentity);
        var book = Node("book:1", "books-metadata", ProposalKind.Book, "Volume One", bookIdentity, volume);
        var proposal = Node("author:1", "books-metadata", ProposalKind.Person, "Author", rootIdentity, book);
        var review = Review(
            "books-metadata",
            RequestMediaKind.Author,
            rootIdentity,
            proposal,
            [
                Target(proposal, RequestMediaKind.Author, rootIdentity),
                Target(book, RequestMediaKind.Book, bookIdentity),
                Target(volume, RequestMediaKind.Book, volumeIdentity)
            ]);
        var reviews = new FakeReviewSource(_ => review);
        var (service, writer, acquisitions, _, _) = ReviewedService(proposal, reviews);

        await service.CommitReviewedAsync(
            new ReviewedRequestCommitRequest(
                RequestMediaKind.Author,
                "books-metadata",
                rootIdentity,
                review.Revision,
                [book.ProposalId]),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Single(writer.Ensured, call => call.Kind == EntityKind.Book);
        Assert.DoesNotContain(writer.Ensured, call => call.ItemId == volumeIdentity.Value);
        Assert.Single(acquisitions.Created);
        Assert.Single(reviews.RevalidateCalls);
    }

    [Fact]
    public async Task ReviewedSeriesHydratesEpisodePhantomsThroughSamePluginAndPerNodeIdentities() {
        var pluginId = "series-metadata";
        var seriesIdentity = new ExternalIdentity("tmdb", "Series:One");
        var seasonIdentity = new ExternalIdentity("tvdb", "Season:One");
        var episodeIdentity = new ExternalIdentity("episode-db", "Episode:One");
        var seasonShell = Node(
            "season:one",
            pluginId,
            ProposalKind.VideoSeason,
            "Season 1",
            seasonIdentity,
            new Dictionary<string, int> { [EntityPositionCodes.Season] = 1 });
        var series = Node("series:one", pluginId, ProposalKind.VideoSeries, "Series", seriesIdentity, seasonShell);
        var seriesReview = Review(
            pluginId,
            RequestMediaKind.Series,
            seriesIdentity,
            series,
            [
                Target(series, RequestMediaKind.Series, seriesIdentity),
                Target(seasonShell, RequestMediaKind.Season, seasonIdentity, position: 1)
            ]);

        var episode = Node(
            "episode:one",
            pluginId,
            ProposalKind.Video,
            "Pilot",
            episodeIdentity,
            new Dictionary<string, int> { [EntityPositionCodes.Episode] = 1 });
        var hydratedSeason = Node(
            seasonShell.ProposalId,
            pluginId,
            ProposalKind.VideoSeason,
            "Season 1",
            seasonIdentity,
            new Dictionary<string, int> { [EntityPositionCodes.Season] = 1 },
            episode);
        var seasonReview = Review(
            pluginId,
            RequestMediaKind.Season,
            seasonIdentity,
            hydratedSeason,
            [
                Target(hydratedSeason, RequestMediaKind.Season, seasonIdentity),
                Target(episode, RequestMediaKind.Episode, episodeIdentity, position: 1)
            ]);
        var reviews = new FakeReviewSource(request => request.Kind switch {
            RequestMediaKind.Series when request.ExternalIdentity == seriesIdentity => seriesReview,
            RequestMediaKind.Season when request.ExternalIdentity == seasonIdentity => seasonReview,
            _ => null
        });
        var (service, writer, acquisitions, _, _) = ReviewedService(series, reviews);

        await service.CommitReviewedAsync(
            new ReviewedRequestCommitRequest(
                RequestMediaKind.Series,
                pluginId,
                seriesIdentity,
                seriesReview.Revision,
                [seasonShell.ProposalId]),
            hideNsfw: false,
            CancellationToken.None);

        var seasonAcquisition = Assert.Single(acquisitions.Created);
        Assert.Equal("tvdb", seasonAcquisition.IdentityNamespace);
        Assert.Equal("Season:One", seasonAcquisition.IdentityValue);
        var episodeCall = Assert.Single(writer.Ensured, call => call.Kind == EntityKind.Video);
        Assert.Equal("episode-db", episodeCall.IdentityNamespace);
        Assert.Equal("Episode:One", episodeCall.ItemId);
        Assert.Equal(FakeWantedEntityWriter.EntityIdFor("Season:One"), episodeCall.ParentEntityId);
        Assert.Collection(
            reviews.RevalidateCalls,
            call => {
                Assert.Equal(pluginId, call.PluginId);
                Assert.Equal(seriesIdentity, call.ExternalIdentity);
            },
            call => {
                Assert.Equal(pluginId, call.PluginId);
                Assert.Equal(seasonIdentity, call.ExternalIdentity);
            });
    }

    [Fact]
    public async Task ReviewedSeriesDescendantLookupFailureLeavesNoPartialWrites() {
        var pluginId = "series-metadata";
        var seriesIdentity = new ExternalIdentity("tmdb", "Series:Preflight");
        var seasonIdentity = new ExternalIdentity("tvdb", "Season:Preflight");
        var season = Node("season:preflight", pluginId, ProposalKind.VideoSeason, "Season 1", seasonIdentity);
        var series = Node("series:preflight", pluginId, ProposalKind.VideoSeries, "Series", seriesIdentity, season);
        var seriesReview = Review(
            pluginId,
            RequestMediaKind.Series,
            seriesIdentity,
            series,
            [
                Target(series, RequestMediaKind.Series, seriesIdentity),
                Target(season, RequestMediaKind.Season, seasonIdentity)
            ]);
        var reviews = new FakeReviewSource(request =>
            request.Kind == RequestMediaKind.Series ? seriesReview : null);
        var (service, writer, acquisitions, monitors, suppressions) = ReviewedService(series, reviews);

        var response = await service.CommitReviewedAsync(
            new ReviewedRequestCommitRequest(
                RequestMediaKind.Series,
                pluginId,
                seriesIdentity,
                seriesReview.Revision,
                [season.ProposalId]),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Null(response);
        Assert.Equal(2, reviews.RevalidateCalls.Count);
        Assert.Empty(writer.Ensured);
        Assert.Empty(writer.Applied);
        Assert.Empty(acquisitions.Created);
        Assert.Empty(monitors.EntityMonitors);
        Assert.Empty(monitors.AcquisitionMonitors);
        Assert.Empty(suppressions.Cleared);
    }

    [Fact]
    public async Task ReviewedFuturePresetCreatesContainerMonitorWithoutAcquisitions() {
        var rootIdentity = new ExternalIdentity("tmdb", "Series:Future");
        var seasonIdentity = new ExternalIdentity("tmdb", "Series:Future:S1");
        var season = Node("season:1", "series-metadata", ProposalKind.VideoSeason, "Season 1", seasonIdentity);
        var proposal = Node("series:future", "series-metadata", ProposalKind.VideoSeries, "Series", rootIdentity, season);
        var review = Review(
            "series-metadata",
            RequestMediaKind.Series,
            rootIdentity,
            proposal,
            [
                Target(proposal, RequestMediaKind.Series, rootIdentity),
                Target(season, RequestMediaKind.Season, seasonIdentity, position: 1)
            ]);
        var reviews = new FakeReviewSource(_ => review);
        var (service, writer, acquisitions, monitors, _) = ReviewedService(proposal, reviews);

        var response = await service.CommitReviewedAsync(
            new ReviewedRequestCommitRequest(
                RequestMediaKind.Series,
                "series-metadata",
                rootIdentity,
                review.Revision,
                SelectedProposalIds: [],
                Preset: MonitorPreset.Future),
            hideNsfw: false,
            CancellationToken.None);

        Assert.NotNull(response!.ContainerEntityId);
        Assert.Single(writer.Ensured, call => call.Kind == EntityKind.VideoSeries);
        Assert.DoesNotContain(writer.Ensured, call => call.Kind == EntityKind.VideoSeason);
        Assert.Empty(acquisitions.Created);
        Assert.Equal(MonitorPreset.Future, Assert.Single(monitors.EntityMonitorPresets));
    }

    [Fact]
    public async Task ReviewedBookVolumeSelectionCreatesStandaloneWantedVolume() {
        var rootIdentity = new ExternalIdentity("openlibrary", "series:1");
        var volumeIdentity = new ExternalIdentity("openlibrary-work", "OL:Work:2");
        var volume = Node("volume:Two", "books-metadata", ProposalKind.Book, "Volume Two", volumeIdentity);
        var proposal = Node("series:1", "books-metadata", ProposalKind.Book, "Series", rootIdentity, volume);
        var review = Review(
            "books-metadata",
            RequestMediaKind.Book,
            rootIdentity,
            proposal,
            [
                Target(proposal, RequestMediaKind.Book, rootIdentity),
                Target(volume, RequestMediaKind.Book, volumeIdentity)
            ]);
        var reviews = new FakeReviewSource(_ => review);
        var (service, writer, acquisitions, _, _) = ReviewedService(proposal, reviews);

        await service.CommitReviewedAsync(
            new ReviewedRequestCommitRequest(
                RequestMediaKind.Book,
                "books-metadata",
                rootIdentity,
                review.Revision,
                [volume.ProposalId]),
            hideNsfw: false,
            CancellationToken.None);

        var wantedVolume = Assert.Single(writer.Ensured);
        Assert.Null(wantedVolume.ParentEntityId);
        Assert.Equal("openlibrary-work", wantedVolume.IdentityNamespace);
        Assert.Equal("OL:Work:2", wantedVolume.ItemId);
        Assert.Equal("openlibrary-work", Assert.Single(acquisitions.Created).IdentityNamespace);
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
        var proposals = new FakeProposalSource(proposal);
        var service = new RequestCommitService(proposals, new NullReviewSource(), writer, acquisitions, monitors, suppressions);
        return (service, writer, acquisitions, monitors, suppressions);
    }

    private static (RequestCommitService Service, FakeWantedEntityWriter Writer, FakeAcquisitionRequestService Acquisitions, FakeMonitorStore Monitors, FakeSuppressionStore Suppressions) ReviewedService(
        EntityMetadataProposal proposal,
        FakeReviewSource reviews) {
        var writer = new FakeWantedEntityWriter();
        var acquisitions = new FakeAcquisitionRequestService();
        var monitors = new FakeMonitorStore();
        var suppressions = new FakeSuppressionStore();
        var service = new RequestCommitService(
            new FakeProposalSource(proposal),
            reviews,
            writer,
            acquisitions,
            monitors,
            suppressions);
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

    private static EntityMetadataProposal Node(
        string proposalId,
        string pluginId,
        ProposalKind kind,
        string title,
        ExternalIdentity identity,
        params EntityMetadataProposal[] children) =>
        Node(proposalId, pluginId, kind, title, identity, new Dictionary<string, int>(), children);

    private static EntityMetadataProposal Node(
        string proposalId,
        string pluginId,
        ProposalKind kind,
        string title,
        ExternalIdentity identity,
        IReadOnlyDictionary<string, int> positions,
        params EntityMetadataProposal[] children) =>
        new(
            proposalId,
            pluginId,
            kind,
            1,
            "external-id",
            new EntityMetadataPatch(
                title,
                null,
                new Dictionary<string, string> { [identity.Namespace] = identity.Value },
                [],
                [],
                null,
                [],
                new Dictionary<string, string>(),
                new Dictionary<string, int>(),
                positions,
                null),
            [],
            children,
            [],
            null,
            []);

    private static RequestReviewTarget Target(
        EntityMetadataProposal proposal,
        RequestMediaKind kind,
        ExternalIdentity identity,
        EntityKind? entityKind = null,
        int? position = null) =>
        new(
            proposal.ProposalId,
            kind,
            entityKind ?? proposal.TargetKind.ToEntityKind(),
            identity,
            Requestable: true,
            Position: position);

    private static RequestReviewResponse Review(
        string pluginId,
        RequestMediaKind kind,
        ExternalIdentity rootIdentity,
        EntityMetadataProposal proposal,
        IReadOnlyList<RequestReviewTarget> targets) =>
        new(
            pluginId,
            rootIdentity,
            proposal.TargetKind.ToEntityKind(),
            kind,
            proposal,
            RequestProposalRevision.Compute(proposal),
            targets);

    private static EntityMetadataProposal Rekey(
        EntityMetadataProposal proposal,
        string identityNamespace,
        string pluginId) {
        var externalIds = proposal.Patch.ExternalIds.Values
            .Take(1)
            .ToDictionary(_ => identityNamespace, value => value, StringComparer.Ordinal);
        return proposal with {
            Provider = pluginId,
            Patch = proposal.Patch with { ExternalIds = externalIds },
            Children = proposal.Children.Select(child => Rekey(child, identityNamespace, pluginId)).ToArray()
        };
    }

    /// <summary>Resolves any node of the proposal tree by its provider item id — the shape of a plugin's per-item lookups.</summary>
    private sealed class FakeProposalSource(EntityMetadataProposal proposal) : IPluginRequestProposalSource {
        public Task<EntityMetadataProposal?> ResolveProposalAsync(
            RequestKindDescriptor descriptor,
            Prismedia.Application.Plugins.PluginIdentityRoute route,
            bool hideNsfw,
            bool includeChildren,
            CancellationToken cancellationToken) =>
            ResolveProposalAsync(descriptor, route.Identity, hideNsfw, includeChildren, cancellationToken);

        public Task<EntityMetadataProposal?> ResolveProposalAsync(
            RequestKindDescriptor descriptor, ExternalIdentity identity, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) =>
            Task.FromResult(FindByItemId(proposal, identity.Namespace, identity.Value));

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

    private sealed class NullReviewSource : IPluginRequestReviewSource {
        public Task<RequestReviewResponse?> ReviewAsync(
            RequestReviewRequest request,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<RequestReviewResponse?>(null);

        public Task<RequestReviewResponse?> RevalidateAsync(
            RequestReviewRequest request,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult<RequestReviewResponse?>(null);
    }

    private sealed class FakeReviewSource(
        Func<RequestReviewRequest, RequestReviewResponse?> resolve) : IPluginRequestReviewSource {
        public List<RequestReviewRequest> ReviewCalls { get; } = [];
        public List<RequestReviewRequest> RevalidateCalls { get; } = [];

        public Task<RequestReviewResponse?> ReviewAsync(
            RequestReviewRequest request,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            ReviewCalls.Add(request);
            return Task.FromResult(resolve(request));
        }

        public Task<RequestReviewResponse?> RevalidateAsync(
            RequestReviewRequest request,
            bool hideNsfw,
            CancellationToken cancellationToken) {
            RevalidateCalls.Add(request);
            return Task.FromResult(resolve(request));
        }
    }

    private sealed class FakeWantedEntityWriter : IWantedEntityWriter {
        public sealed record EnsureCall(
            EntityKind Kind,
            string IdentityNamespace,
            string ItemId,
            string Title,
            Guid? ParentEntityId,
            bool MatchTitleKindWide);
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

        public Task<WantedEntityResult> EnsureAsync(EntityKind kind, ExternalIdentity identity, string title, Guid? parentEntityId, bool matchTitleKindWide, CancellationToken cancellationToken) {
            Ensured.Add(new EnsureCall(kind, identity.Namespace, identity.Value, title, parentEntityId, matchTitleKindWide));
            var hasFile = ExistingWithFile.Contains(identity.Value);
            var created = !hasFile && !ExistingWanted.Contains(identity.Value);
            return Task.FromResult(new WantedEntityResult(EntityIdFor(identity.Value), created, hasFile));
        }

        public Task ApplyProposalAsync(Guid entityId, EntityMetadataProposal proposal, CancellationToken cancellationToken) {
            Applied.Add(new ApplyCall(entityId, proposal));
            return Task.CompletedTask;
        }

        public Task<bool> DeleteIfWantedAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        /// <summary>Container refs returned by GetContainerAsync, for sync/entity-request tests.</summary>
        public Dictionary<Guid, MonitorableContainer> Containers { get; } = [];

        /// <summary>Legacy single container slot kept for simple sync tests.</summary>
        public MonitorableContainer? Container { get; set; }

        public Task<MonitorableContainer?> GetContainerAsync(Guid entityId, CancellationToken cancellationToken) {
            if (Containers.TryGetValue(entityId, out var container)) {
                return Task.FromResult<MonitorableContainer?>(container);
            }

            return Task.FromResult(Container?.EntityId == entityId ? Container : null);
        }

        /// <summary>Wanted child ids per parent, for missing-children fallback tests.</summary>
        public Dictionary<Guid, IReadOnlyList<Guid>> WantedChildren { get; } = [];

        public Task<IReadOnlyList<Guid>> ListWantedChildIdsAsync(Guid parentEntityId, EntityKind childKind, CancellationToken cancellationToken) =>
            Task.FromResult(WantedChildren.GetValueOrDefault(parentEntityId, []));

        public Task<IReadOnlyList<Guid>> ListChildIdsAsync(Guid parentEntityId, EntityKind childKind, CancellationToken cancellationToken) =>
            Task.FromResult(WantedChildren.GetValueOrDefault(parentEntityId, []));
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
        public Task<bool> RetargetAsync(Guid fromAcquisitionId, Guid toAcquisitionId, CancellationToken cancellationToken) => Task.FromResult(false);
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

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken, bool preserveWantedLoop = false) {
            Deleted.Add(id);
            return Task.FromResult(true);
        }

        public Task<Guid?> ReacquireAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSuppressionStore : IWantedSuppressionStore {
        public List<string> Suppressed { get; } = [];
        public List<string> Cleared { get; } = [];

        public Task SuppressAsync(IReadOnlyList<ExternalIdentity> identities, EntityKind kind, string title, CancellationToken cancellationToken) {
            Suppressed.AddRange(identities.Select(Key));
            return Task.CompletedTask;
        }

        public Task<IReadOnlySet<ExternalIdentity>> FilterSuppressedAsync(
            IReadOnlyList<ExternalIdentity> identities,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<ExternalIdentity>>(identities
                .Where(identity => Suppressed.Contains(Key(identity)))
                .ToHashSet());

        public Task ClearAsync(IReadOnlyList<ExternalIdentity> identities, CancellationToken cancellationToken) {
            foreach (var identity in identities) {
                var key = Key(identity);
                Cleared.Add(key);
                Suppressed.Remove(key);
            }

            return Task.CompletedTask;
        }

        private static string Key(ExternalIdentity identity) =>
            $"{identity.Namespace}:{identity.Value}";
    }
}
