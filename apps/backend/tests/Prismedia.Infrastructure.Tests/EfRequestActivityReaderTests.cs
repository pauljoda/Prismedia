using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Tests;

public sealed class EfRequestActivityReaderTests : IDisposable {
    private static readonly JsonSerializerOptions Json = PluginProcessTransport.JsonOptions;
    private readonly string keyRoot = Path.Combine(Path.GetTempPath(), $"prismedia-activity-{Guid.NewGuid():N}");

    [Fact]
    public async Task EqualTimestampsPageDeterministicallyAndAHoldingSupersedesItsRequest() {
        await using var db = Context();
        var connection = AddConnection(db, ConnectionStatus.Ready, lastCheckedAt: null);
        var root = AddRoot(db, false);
        var occurredAt = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        var first = AddRequest(db, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), connection.Id, root.Id, occurredAt, "First");
        var second = AddRequest(db, Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), connection.Id, root.Id, occurredAt, "Second");
        var superseded = AddRequest(db, Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), connection.Id, root.Id, occurredAt, "Superseded");
        AddHolding(db, superseded, occurredAt, "Tracked");
        await db.SaveChangesAsync();
        var reader = Reader(db);

        var pageOne = await reader.ListAsync(null, null, 2, false, default);
        var pageTwo = await reader.ListAsync(null, pageOne.NextCursor, 2, false, default);

        Assert.Equal(new[] { first.Id, second.Id }, pageOne.Items.Select(item => item.Id));
        Assert.NotNull(pageOne.NextCursor);
        var tracked = Assert.Single(pageTwo.Items);
        Assert.Equal(superseded.Id, tracked.Id);
        Assert.NotNull(tracked.Holding);
        Assert.Null(tracked.Request);
        Assert.Null(pageTwo.NextCursor);
        var source = Assert.Single(pageOne.Sources);
        Assert.True(source.IsStale);
        Assert.Null(source.LastCheckedAt);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            reader.ListAsync(null, pageOne.NextCursor, 2, true, default));
    }

    [Fact]
    public async Task ConnectionFilterAndPersistedHealthDoNotRequireRemoteAvailability() {
        await using var db = Context();
        var offline = AddConnection(db, ConnectionStatus.Unavailable, DateTimeOffset.UtcNow, "Connection refused");
        var other = AddConnection(db, ConnectionStatus.Ready, DateTimeOffset.UtcNow);
        var root = AddRoot(db, false);
        AddRequest(db, Guid.NewGuid(), offline.Id, root.Id, DateTimeOffset.UtcNow, "Retained offline request");
        AddRequest(db, Guid.NewGuid(), other.Id, root.Id, DateTimeOffset.UtcNow.AddMinutes(1), "Other request");
        await db.SaveChangesAsync();

        var page = await Reader(db).ListAsync(offline.Id, null, 10, false, default);

        var retained = Assert.Single(page.Items);
        Assert.Equal(offline.Id, retained.ConnectionId);
        var source = Assert.Single(page.Sources);
        Assert.True(source.IsStale);
        Assert.Equal("Connection refused", source.Problem);
    }

    [Fact]
    public async Task OlderAttentionWorkPrecedesFrequentlyCheckedHealthyHoldings() {
        await using var db = Context();
        var connection = AddConnection(db, ConnectionStatus.Ready, DateTimeOffset.UtcNow);
        var root = AddRoot(db, false);
        var attention = AddRequest(db, Guid.NewGuid(), connection.Id, root.Id,
            DateTimeOffset.UtcNow.AddDays(-30), "Needs review", ManagedRequestPhase.Rejected);
        var trackedRequest = AddRequest(db, Guid.NewGuid(), connection.Id, root.Id,
            DateTimeOffset.UtcNow.AddDays(-60), "Tracked request");
        AddHolding(db, trackedRequest, DateTimeOffset.UtcNow, "Frequently checked holding");
        await db.SaveChangesAsync();

        var page = await Reader(db).ListAsync(null, null, 1, false, default);

        Assert.Equal(attention.Id, Assert.Single(page.Items).Id);
        Assert.Equal(ManagedRequestPhase.Rejected, page.Items[0].Request!.Phase);
    }

    [Fact]
    public async Task ReviewRequestSupersedesItsHoldingWhileWaitingMessagesStayInProgress() {
        await using var db = Context();
        var connection = AddConnection(db, ConnectionStatus.Ready, DateTimeOffset.UtcNow);
        var root = AddRoot(db, false);
        var now = DateTimeOffset.UtcNow;
        var review = AddRequest(db, Guid.NewGuid(), connection.Id, root.Id,
            now.AddDays(-10), "Paused for review", ManagedRequestPhase.AwaitingFiles, reviewRequired: true);
        AddHolding(db, review, now, "Paused holding", ManagedTrackingStatus.WaitingForFiles);
        var waiting = AddRequest(db, Guid.NewGuid(), connection.Id, root.Id,
            now, "Still searching", ManagedRequestPhase.AwaitingFiles, problem: "The manager is still searching.");
        await db.SaveChangesAsync();

        var page = await Reader(db).ListAsync(null, null, 10, false, default);

        Assert.Equal(new[] { review.Id, waiting.Id }, page.Items.Select(item => item.Id));
        var reviewItem = page.Items[0];
        Assert.NotNull(reviewItem.Request);
        Assert.True(reviewItem.Request.ReviewRequired);
        Assert.Equal(review.Id, reviewItem.Request.HoldingId);
        Assert.DoesNotContain(page.Items, item => item.Holding?.Id == review.Id);
    }

    [Fact]
    public async Task PostgreSqlTranslatesTheStrictCursorSeek() {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var connection = AddConnection(db, ConnectionStatus.Ready, DateTimeOffset.UtcNow);
        var root = AddRoot(db, false);
        var occurredAt = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        var first = AddRequest(db, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), connection.Id, root.Id, occurredAt, "First");
        var second = AddRequest(db, Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), connection.Id, root.Id, occurredAt, "Second");
        var attention = AddRequest(db, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), connection.Id, root.Id,
            occurredAt.AddYears(-1), "Review first", ManagedRequestPhase.AwaitingFiles, reviewRequired: true);
        AddHolding(db, attention, occurredAt, "Waiting review", ManagedTrackingStatus.WaitingForFiles);
        await db.SaveChangesAsync();
        var reader = Reader(db);

        var pageOne = await reader.ListAsync(connection.Id, null, 1, false, default);
        var pageTwo = await reader.ListAsync(connection.Id, pageOne.NextCursor, 1, false, default);
        var pageThree = await reader.ListAsync(connection.Id, pageTwo.NextCursor, 1, false, default);

        Assert.Equal(attention.Id, Assert.Single(pageOne.Items).Id);
        Assert.Equal(first.Id, Assert.Single(pageTwo.Items).Id);
        Assert.Equal(second.Id, Assert.Single(pageThree.Items).Id);
    }

    [Fact]
    public async Task NsfwLibraryWithholdsEncryptedTransferMetadata() {
        await using var db = Context();
        var connection = AddConnection(db, ConnectionStatus.Ready, DateTimeOffset.UtcNow);
        var root = AddRoot(db, true);
        var transfer = IntegrationTransfer.CreateSourceDownload(Guid.NewGuid(), connection.Id);
        var plan = new IntegrationTransferPlan(
            "Private title",
            EntityKind.Book,
            root.Id,
            Path.GetTempPath(),
            "owner",
            new string('a', 64),
            new(new("item", "https://catalog.test/private", EntityKind.Book), "offer"));
        var protector = new TransferPlanProtector(keyRoot);
        db.IntegrationTransfers.Add(new() {
            Id = transfer.State.OperationId,
            ConnectionId = connection.Id,
            Revision = transfer.State.Revision,
            Phase = transfer.State.Phase,
            StateJson = JsonSerializer.Serialize(transfer.State, Json),
            ProtectedPlan = protector.Protect(connection.Id, transfer.State.OperationId, JsonSerializer.Serialize(plan, Json)),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var hidden = await new EfRequestActivityReader(db, protector, TimeProvider.System)
            .ListAsync(null, null, 10, true, default);
        var visible = await new EfRequestActivityReader(db, protector, TimeProvider.System)
            .ListAsync(null, null, 10, false, default);

        Assert.Empty(hidden.Items);
        Assert.Equal("Private title", Assert.Single(visible.Items).Transfer!.Title);
    }

    private EfRequestActivityReader Reader(PrismediaDbContext db) =>
        new(db, new TransferPlanProtector(keyRoot), TimeProvider.System);

    private static PrismediaDbContext Context() => new(
        new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"request-activity-{Guid.NewGuid():N}")
            .Options);

    private static IntegrationConnectionRow AddConnection(
        PrismediaDbContext db,
        ConnectionStatus status,
        DateTimeOffset? lastCheckedAt,
        string? lastError = null) {
        var row = new IntegrationConnectionRow {
            Id = Guid.NewGuid(),
            PluginId = "fixture-manager",
            Name = $"Connection {Guid.NewGuid():N}",
            BaseUrl = "https://manager.test/",
            Enabled = true,
            Revision = 1,
            Status = status,
            LastCheckedAt = lastCheckedAt,
            LastError = lastError
        };
        db.IntegrationConnections.Add(row);
        return row;
    }

    private static LibraryRootRow AddRoot(PrismediaDbContext db, bool isNsfw) {
        var row = new LibraryRootRow {
            Id = Guid.NewGuid(),
            Path = $"/media/{Guid.NewGuid():N}",
            Label = "Fixture",
            IsNsfw = isNsfw,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.LibraryRoots.Add(row);
        return row;
    }

    private static ManagedRequestRow AddRequest(
        PrismediaDbContext db,
        Guid id,
        Guid connectionId,
        Guid rootId,
        DateTimeOffset occurredAt,
        string title,
        ManagedRequestPhase phase = ManagedRequestPhase.PendingCreation,
        bool reviewRequired = false,
        string? problem = null) {
        var entityId = Guid.NewGuid();
        db.Entities.Add(new() { Id = entityId, KindCode = EntityKind.Movie.ToCode(), Title = title, IsWanted = true });
        var work = new ManagedLookupInput(EntityKind.Movie, new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = id.ToString("N") });
        var input = new CreateManagedRequestInput(id, entityId, rootId, work, "profile", true, true);
        var state = new ManagedRequestState(id, connectionId, entityId, rootId, 1, phase,
            phase == ManagedRequestPhase.AwaitingFiles ? "remote" : null, reviewRequired);
        var plan = new ManagedRequestPlan(input, new(id, work, "profile", "root", "/media"), title, "fixture");
        var row = new ManagedRequestRow {
            Id = id,
            ConnectionId = connectionId,
            EntityId = entityId,
            LibraryRootId = rootId,
            Revision = state.Revision,
            Phase = state.Phase,
            StateJson = JsonSerializer.Serialize(state, Json),
            PlanJson = JsonSerializer.Serialize(plan, Json),
            CreatedAt = occurredAt,
            UpdatedAt = occurredAt,
            Problem = problem
        };
        db.ManagedRequests.Add(row);
        return row;
    }

    private static void AddHolding(
        PrismediaDbContext db,
        ManagedRequestRow request,
        DateTimeOffset occurredAt,
        string title,
        ManagedTrackingStatus status = ManagedTrackingStatus.Tracking) {
        var item = new ManagedItemInput(EntityKind.Movie, "remote", new Dictionary<string, string> { [ExternalIdProviders.Tmdb] = "1" });
        db.ManagedHoldings.Add(new() {
            Id = request.Id,
            ConnectionId = request.ConnectionId,
            LibraryRootId = request.LibraryRootId,
            Kind = EntityKind.Movie,
            RemoteId = item.RemoteId,
            Title = title,
            ItemJson = JsonSerializer.Serialize(item, Json),
            SelectionsJson = "[]",
            TargetsJson = "[]",
            Status = status,
            Revision = 1,
            LastCheckedAt = occurredAt,
            NextCheckAt = occurredAt
        });
    }

    public void Dispose() {
        if (Directory.Exists(keyRoot)) Directory.Delete(keyRoot, true);
    }
}
