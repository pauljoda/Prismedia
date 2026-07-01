using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Requests;

/// <summary>
/// Covers the request commit — the Identify apply run on entities that don't exist on disk yet: an
/// author commit creates the wanted author + picked books and one acquisition per book (each linked to
/// its wanted entity), owned/in-flight picks are skipped transparently, the proposal is applied filtered
/// to the picked works, and book commits handle both the standalone and series-volume shapes.
/// </summary>
public sealed class RequestCommitServiceTests {
    private const string Provider = "openlibrary";

    [Fact]
    public async Task AuthorCommitCreatesTheWantedAuthorAndPickedBooksAndAcquiresEach() {
        var proposal = AuthorProposal("Brandon Sanderson", Book("W1", "Elantris"), Book("W2", "Warbreaker"), Book("W3", "Skipped"));
        var (service, writer, acquisitions) = Service(authorProposal: proposal);

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Author, $"{Provider}:A1", [$"{Provider}:W1", $"{Provider}:W2"]),
            hideNsfw: false, CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(response!.ContainerEntityId);

        var author = Assert.Single(writer.Ensured, call => call.Kind == EntityKind.BookAuthor);
        Assert.Equal("Brandon Sanderson", author.Title);
        Assert.Null(author.ParentEntityId);

        var books = writer.Ensured.Where(call => call.Kind == EntityKind.Book).ToArray();
        Assert.Equal(["W1", "W2"], books.Select(call => call.ItemId).ToArray());
        Assert.All(books, call => Assert.Equal(response.ContainerEntityId, call.ParentEntityId));

        Assert.Equal(2, response.Items.Count);
        Assert.All(response.Items, item => Assert.Equal(RequestCommitOutcome.Requested, item.Outcome));
        Assert.Equal(2, acquisitions.Created.Count);
        Assert.All(acquisitions.Created, request => Assert.Equal("Brandon Sanderson", request.Author));
        Assert.Equal(
            response.Items.Select(item => item.EntityId).ToArray(),
            acquisitions.Created.Select(request => request.EntityId).ToArray());
    }

    [Fact]
    public async Task AuthorCommitAppliesTheProposalFilteredToThePickedWorks() {
        var proposal = AuthorProposal("Author", Book("W1", "Picked"), Book("W2", "Unpicked"));
        var (service, writer, _) = Service(authorProposal: proposal);

        await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Author, $"{Provider}:A1", [$"{Provider}:W1"]),
            hideNsfw: false, CancellationToken.None);

        var applied = Assert.Single(writer.Applied);
        var child = Assert.Single(applied.Proposal.Children);
        Assert.Equal("Picked", child.Patch.Title);
    }

    [Fact]
    public async Task OwnedPickIsReportedAlreadyOwnedAndNeitherAppliedNorAcquired() {
        var proposal = AuthorProposal("Author", Book("W1", "Owned"), Book("W2", "New"));
        var (service, writer, acquisitions) = Service(authorProposal: proposal);
        writer.ExistingWithFile.Add("W1");

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Author, $"{Provider}:A1", [$"{Provider}:W1", $"{Provider}:W2"]),
            hideNsfw: false, CancellationToken.None);

        Assert.Equal(
            [RequestCommitOutcome.AlreadyOwned, RequestCommitOutcome.Requested],
            response!.Items.Select(item => item.Outcome).ToArray());
        Assert.Equal("New", Assert.Single(acquisitions.Created).Title);

        // The owned book is excluded from the author apply so a request can't overwrite owned metadata.
        var applied = Assert.Single(writer.Applied);
        Assert.Equal("New", Assert.Single(applied.Proposal.Children).Patch.Title);
    }

    [Fact]
    public async Task InFlightWantedPickIsReportedAlreadyRequestedWithoutANewAcquisition() {
        var proposal = AuthorProposal("Author", Book("W1", "InFlight"));
        var (service, writer, acquisitions) = Service(authorProposal: proposal);
        writer.ExistingWanted.Add("W1");
        acquisitions.EntitiesWithAcquisitions.Add(FakeWantedEntityWriter.EntityIdFor("W1"));

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Author, $"{Provider}:A1", [$"{Provider}:W1"]),
            hideNsfw: false, CancellationToken.None);

        Assert.Equal(RequestCommitOutcome.AlreadyRequested, Assert.Single(response!.Items).Outcome);
        Assert.Empty(acquisitions.Created);
    }

    [Fact]
    public async Task StandaloneBookCommitCreatesAWantedBookWithItsAcquisitionLinked() {
        var proposal = Book("W1", "Elantris") with {
            Patch = Patch("Elantris", "W1") with { Credits = [new CreditPatch("Brandon Sanderson", "author", null, null)] }
        };
        var (service, writer, acquisitions) = Service(bookProposal: proposal);

        var response = await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Book, $"{Provider}:W1", []),
            hideNsfw: false, CancellationToken.None);

        Assert.Null(response!.ContainerEntityId);
        var item = Assert.Single(response.Items);
        Assert.Equal(RequestCommitOutcome.Requested, item.Outcome);

        var created = Assert.Single(acquisitions.Created);
        Assert.Equal("Elantris", created.Title);
        Assert.Equal("Brandon Sanderson", created.Author);
        Assert.Equal(item.EntityId, created.EntityId);
        Assert.Equal(Provider, created.PluginId);
        Assert.Equal("W1", created.PluginItemId);
        Assert.Single(writer.Applied);
    }

    [Fact]
    public async Task SeriesVolumePicksBecomeStandaloneWantedBooksStampedWithTheSeries() {
        var proposal = Book("W0", "The Stormlight Archive") with {
            Children = [Book("V1", "The Way of Kings"), Book("V2", "Words of Radiance")]
        };
        var (service, writer, acquisitions) = Service(bookProposal: proposal);

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
        var (service, _, _) = Service();

        Assert.Null(await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Book, "missing-separator", []), hideNsfw: false, CancellationToken.None));
        Assert.Null(await service.CommitAsync(
            new RequestCommitRequest(RequestMediaKind.Book, $"{Provider}:W404", []), hideNsfw: false, CancellationToken.None));
    }

    private static (RequestCommitService Service, FakeWantedEntityWriter Writer, FakeAcquisitionRequestService Acquisitions) Service(
        EntityMetadataProposal? authorProposal = null,
        EntityMetadataProposal? bookProposal = null) {
        var writer = new FakeWantedEntityWriter();
        var acquisitions = new FakeAcquisitionRequestService();
        var service = new RequestCommitService(new FakeProposalSource(authorProposal, bookProposal), writer, acquisitions);
        return (service, writer, acquisitions);
    }

    private static EntityMetadataProposal AuthorProposal(string name, params EntityMetadataProposal[] works) =>
        new("p-author", Provider, ProposalKind.Person, null, null, Patch(name, "A1"), [], works, [], null, []);

    private static EntityMetadataProposal Book(string workId, string title) =>
        new($"p-{workId}", Provider, ProposalKind.Book, null, null, Patch(title, workId), [], [], [], null, []);

    private static EntityMetadataPatch Patch(string title, string workId) =>
        new(title, null, new Dictionary<string, string> { [Provider] = workId }, [], [], null, [],
            new Dictionary<string, string>(), new Dictionary<string, int>(), new Dictionary<string, int>(), null);

    private sealed class FakeProposalSource(EntityMetadataProposal? author, EntityMetadataProposal? book) : IPluginRequestProposalSource {
        public Task<EntityMetadataProposal?> ResolveBookProposalAsync(string providerId, string itemId, bool hideNsfw, bool includeChildren, CancellationToken cancellationToken) =>
            Task.FromResult(book?.Patch.ExternalIds.GetValueOrDefault(providerId) == itemId ? book : null);

        public Task<EntityMetadataProposal?> ResolveAuthorProposalAsync(string providerId, string itemId, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult(author);
    }

    private sealed class FakeWantedEntityWriter : IWantedEntityWriter {
        public sealed record EnsureCall(EntityKind Kind, string ItemId, string Title, Guid? ParentEntityId);
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

        public Task<WantedEntityResult> EnsureAsync(EntityKind kind, string providerId, string itemId, string title, Guid? parentEntityId, CancellationToken cancellationToken) {
            Ensured.Add(new EnsureCall(kind, itemId, title, parentEntityId));
            var hasFile = ExistingWithFile.Contains(itemId);
            var created = !hasFile && !ExistingWanted.Contains(itemId);
            return Task.FromResult(new WantedEntityResult(EntityIdFor(itemId), created, hasFile));
        }

        public Task ApplyProposalAsync(Guid entityId, EntityMetadataProposal proposal, CancellationToken cancellationToken) {
            Applied.Add(new ApplyCall(entityId, proposal));
            return Task.CompletedTask;
        }
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
