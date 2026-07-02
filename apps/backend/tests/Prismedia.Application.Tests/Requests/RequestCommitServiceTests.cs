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
    public async Task SeriesCommitIsRefusedWhileItsEngineHasNotLanded() {
        var (service, writer, acquisitions) = Service(Container(ProposalKind.VideoSeries, "Andor", "TV1"));

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Series, $"{Provider}:TV1", []),
            hideNsfw: false, CancellationToken.None);

        Assert.Null(response);
        Assert.Empty(writer.Ensured);
        Assert.Empty(acquisitions.Created);
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
    public async Task MalformedOrUnresolvableExternalIdsReturnNull() {
        var (service, _, _) = Service(Leaf(ProposalKind.Book, "Book", "W1"));

        Assert.Null(await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Book, "missing-separator", []), hideNsfw: false, CancellationToken.None));
        Assert.Null(await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Book, $"{Provider}:W404", []), hideNsfw: false, CancellationToken.None));
    }

    private static (RequestCommitService Service, FakeWantedEntityWriter Writer, FakeAcquisitionRequestService Acquisitions) Service(
        EntityMetadataProposal proposal) {
        var writer = new FakeWantedEntityWriter();
        var acquisitions = new FakeAcquisitionRequestService();
        var service = new RequestCommitService(new FakeProposalSource(proposal), writer, acquisitions);
        return (service, writer, acquisitions);
    }

    private static EntityMetadataProposal Container(ProposalKind kind, string title, string itemId, params EntityMetadataProposal[] works) =>
        new($"p-{itemId}", Provider, kind, null, null, Patch(title, itemId), [], works, [], null, []);

    private static EntityMetadataProposal Leaf(ProposalKind kind, string title, string workId) =>
        new($"p-{workId}", Provider, kind, null, null, Patch(title, workId), [], [], [], null, []);

    private static EntityMetadataPatch Patch(string title, string workId) =>
        new(title, null, new Dictionary<string, string> { [Provider] = workId }, [], [], null, [],
            new Dictionary<string, string>(), new Dictionary<string, int>(), new Dictionary<string, int>(), null);

    private sealed class FakeProposalSource(EntityMetadataProposal proposal) : IPluginRequestProposalSource {
        public Task<EntityMetadataProposal?> ResolveProposalAsync(
            RequestKindDescriptor descriptor, string providerId, string itemId, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) =>
            Task.FromResult(proposal.Patch.ExternalIds.GetValueOrDefault(providerId) == itemId ? proposal : null);
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
    }

    private sealed class FakeAcquisitionRequestService : IAcquisitionRequestService {
        public List<AcquisitionCreateRequest> Created { get; } = [];
        public HashSet<Guid> EntitiesWithAcquisitions { get; } = [];

        public Task<AcquisitionSummary> CreateAndSearchAsync(AcquisitionCreateRequest request, CancellationToken cancellationToken) {
            Created.Add(request);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new AcquisitionSummary(
                Guid.NewGuid(), AcquisitionStatus.Pending, null, request.Title, request.Author, request.Series,
                request.Year, request.PosterUrl, null, now, now, request.Description, request.Kind, request.EntityId));
        }

        public Task<bool> AnyForEntityAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(EntitiesWithAcquisitions.Contains(entityId));
    }
}
