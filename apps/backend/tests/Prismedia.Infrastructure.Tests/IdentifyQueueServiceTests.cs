using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Persistence;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Processes;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Tests;

public sealed class IdentifyQueueServiceTests : IDisposable {
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"prismedia-identify-queue-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReidentifyFallsBackToSearchWhenStoredIdLookupFindsNothing() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Stored Movie");
        // A prior identify persisted a provider id, so ResolveAction routes this re-run to lookup-id.
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Provider = "tmdb",
            Value = "123",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var executor = new LookupMissSearchHitProcessExecutor();
        var identify = CreateIdentifyService(db, executor, _tempRoot);

        var response = await identify.IdentifyAsync(entityId, "tmdb", null, null, hideNsfw: false, CancellationToken.None);

        // The id lookup found nothing, so the cascade fell back to a clean search the way the very
        // first identify ran before any id was stored — and that search matched.
        Assert.True(response.Ok);
        Assert.NotNull(response.Result?.Patch);
        Assert.Equal("Stored Movie via search", response.Result!.Patch!.Title);
        Assert.Equal(["lookup-id", "search"], executor.Actions);
    }

    [Fact]
    public async Task AddAsyncCreatesDurableSearchItemForEntity() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        SeedEntity(db, entityId, "video-series", "Mystery Show");
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new CandidateProcessExecutor(), _tempRoot);

        var item = await service.AddAsync(entityId, CancellationToken.None);

        Assert.Equal(entityId, item.EntityId);
        Assert.Equal(EntityKind.VideoSeries, item.EntityKind);
        Assert.Equal("Mystery Show", item.Title);
        Assert.Equal("search", item.State);
        Assert.Null(item.Provider);
        Assert.Empty(item.Candidates);
        Assert.Null(item.Proposal);
        Assert.Single(await db.IdentifyQueueItems.ToArrayAsync());
    }

    [Fact]
    public async Task SearchAsyncKeepsProviderCandidatesInSearchState() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Ambiguous Movie");
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new CandidateThenProposalProcessExecutor(), _tempRoot);
        await service.AddAsync(entityId, CancellationToken.None);

        var item = await SearchToCompletionAsync(service, db, entityId, new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery("Ambiguous", null, null)), hideNsfw: false, CancellationToken.None);

        Assert.Equal("search", item.State);
        Assert.Equal("tmdb", item.Provider);
        Assert.Null(item.Proposal);
        var candidate = Assert.Single(item.Candidates);
        Assert.Equal("Ambiguous Movie (2005)", candidate.Title);
        var persisted = await db.IdentifyQueueItems.SingleAsync();
        Assert.Equal(IdentifyQueueState.Search, persisted.State);
        Assert.NotNull(persisted.CandidatesJson);
        Assert.Null(persisted.ProposalJson);
    }

    [Fact]
    public async Task SearchAsyncStoresProviderCandidateFields() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("22222222-2222-2222-2222-222222222223");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Candidate Movie");
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new CanonicalCandidateProcessExecutor(), _tempRoot);
        await service.AddAsync(entityId, CancellationToken.None);

        var item = await SearchToCompletionAsync(service, db, entityId, new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery("Candidate", null, null)), hideNsfw: false, CancellationToken.None);

        var candidate = Assert.Single(item.Candidates);
        Assert.Equal("Candidate Movie", candidate.Title);
        Assert.Equal("Candidate overview from the provider.", candidate.Overview);
        Assert.Equal("https://image.example.test/poster.jpg", candidate.PosterUrl);
        Assert.Equal(8.75m, candidate.Popularity);
    }

    [Fact]
    public async Task SearchAsyncStoresConfirmedProposalForReview() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Known Movie");
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new ProposalProcessExecutor(), _tempRoot);
        await service.AddAsync(entityId, CancellationToken.None);

        var item = await SearchToCompletionAsync(service, db, entityId, new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery(null, null, new Dictionary<string, string> { ["tmdb"] = "123" })), hideNsfw: false, CancellationToken.None);

        Assert.Equal("proposal", item.State);
        Assert.Equal("tmdb", item.Provider);
        Assert.Empty(item.Candidates);
        Assert.NotNull(item.Proposal);
        Assert.Equal(entityId, item.Proposal.TargetEntityId);
        Assert.Equal("Known Movie identified", item.Proposal.Patch.Title);
        var persisted = await db.IdentifyQueueItems.SingleAsync();
        Assert.Equal(IdentifyQueueState.Proposal, persisted.State);
        Assert.NotNull(persisted.ProposalJson);
    }

    [Fact]
    public async Task ResolveCandidateAsyncUsesSelectedProviderIdWithoutQueueingAnotherSearch() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("33333333-3333-3333-3333-33333333333a");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Ambiguous Movie");
        await db.SaveChangesAsync();
        var queue = new RecordingJobQueue();
        var service = new IdentifyQueueService(
            db,
            CreateIdentifyService(db, new CandidateThenProposalProcessExecutor(), _tempRoot),
            new InMemoryIdentifyApplyProgressStore(),
            queue);
        await service.AddAsync(entityId, CancellationToken.None);
        var search = await SearchToCompletionAsync(
            service,
            db,
            entityId,
            new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery("Ambiguous", null, null)),
            hideNsfw: false,
            CancellationToken.None);
        var candidate = Assert.Single(search.Candidates);
        queue.Enqueued.Clear();

        var item = await service.ResolveCandidateAsync(
            entityId,
            new IdentifyQueueCandidateRequest("tmdb", candidate),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal("proposal", item.State);
        Assert.Equal("tmdb", item.Provider);
        Assert.Equal("lookup-id", item.Action);
        Assert.Empty(item.Candidates);
        Assert.NotNull(item.Proposal);
        Assert.Equal("Auto-resolved title", item.Proposal!.Patch.Title);
        Assert.NotNull(item.Query?.ExternalIds);
        Assert.True(item.Query!.ExternalIds!.TryGetValue("tmdb", out var selectedId));
        Assert.Equal("2005", selectedId);
        Assert.DoesNotContain(queue.Enqueued, job => job.Type == JobType.IdentifySearch);
    }

    [Fact]
    public async Task ResolveCandidateAsyncDoesNotFallBackToGenericSearchWhenSelectedIdMisses() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("33333333-3333-3333-3333-33333333333b");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Ambiguous Movie");
        await db.SaveChangesAsync();
        var executor = new CandidateThenLookupMissProcessExecutor();
        var queue = new RecordingJobQueue();
        var service = new IdentifyQueueService(
            db,
            CreateIdentifyService(db, executor, _tempRoot),
            new InMemoryIdentifyApplyProgressStore(),
            queue);
        await service.AddAsync(entityId, CancellationToken.None);
        var search = await SearchToCompletionAsync(
            service,
            db,
            entityId,
            new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery("Ambiguous", null, null)),
            hideNsfw: false,
            CancellationToken.None);
        var candidate = Assert.Single(search.Candidates);
        queue.Enqueued.Clear();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResolveCandidateAsync(
                entityId,
                new IdentifyQueueCandidateRequest("tmdb", candidate),
                hideNsfw: false,
                CancellationToken.None));

        Assert.Contains("No TMDB match", error.Message);
        Assert.Equal(["search", "lookup-id"], executor.Actions);
        Assert.DoesNotContain(queue.Enqueued, job => job.Type == JobType.IdentifySearch);
        var row = await db.IdentifyQueueItems.AsNoTracking().SingleAsync();
        Assert.Equal(IdentifyQueueState.Search, row.State);
        Assert.NotNull(row.CandidatesJson);
        Assert.Null(row.ProposalJson);
    }

    [Fact]
    public async Task SearchAsyncKeepsOnlyProviderStructuralChildrenMatchedToLocalChildren() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("33333333-3333-3333-3333-333333333335");
        var episode1Id = Guid.Parse("33333333-3333-3333-3333-333333333336");
        var episode2Id = Guid.Parse("33333333-3333-3333-3333-333333333337");
        SeedProvider(db);
        SeedEntity(db, seriesId, "video-series", "Known Series");
        var episode1 = SeedEntity(db, episode1Id, "video", "Local Episode 1");
        episode1.ParentEntityId = seriesId;
        episode1.SortOrder = 1;
        var episode2 = SeedEntity(db, episode2Id, "video", "Local Episode 2");
        episode2.ParentEntityId = seriesId;
        episode2.SortOrder = 2;
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new StructuralChildrenProcessExecutor(), _tempRoot);
        await service.AddAsync(seriesId, CancellationToken.None);

        var query = new IdentifyQuery(null, null, new Dictionary<string, string> { ["tmdb"] = "series-1" });
        var item = await SearchToCompletionAsync(service, db, 
            seriesId,
            new IdentifyQueueSearchRequest("tmdb", query),
            hideNsfw: false,
            CancellationToken.None);

        // The seed identifies the parent only and enqueues a cascade; its local children stream in as
        // the cascade fully resolves each, so the seed proposal carries none yet.
        Assert.NotNull(item.Proposal);
        Assert.Empty(item.Proposal.Children);
        Assert.True(item.CascadeRunning);

        // Run the cascade (the worker does this in production) and re-read the streamed proposal: each
        // local episode is bound to its provider track by position, and the phantom Episode 3 (with no
        // local file) is dropped.
        var cascadeJobId = (await db.IdentifyQueueItems.AsNoTracking().SingleAsync(i => i.EntityId == seriesId)).CascadeJobId!.Value;
        await service.RunCascadeAsync(
            new IdentifyCascadePayload(seriesId, "tmdb", query, HideNsfw: false),
            cascadeJobId,
            isFinalAttempt: true,
            CancellationToken.None);
        var resolved = await service.GetAsync(seriesId, CancellationToken.None);

        Assert.NotNull(resolved?.Proposal);
        Assert.Equal([episode1Id, episode2Id], resolved!.Proposal!.Children.Select(child => child.TargetEntityId.GetValueOrDefault()).ToArray());
        Assert.Equal(["Episode 1", "Episode 2"], resolved.Proposal.Children.Select(child => child.Patch.Title ?? string.Empty).ToArray());
        Assert.DoesNotContain(resolved.Proposal.Children, child => child.Patch.Title == "Episode 3");
    }

    [Fact]
    public async Task SearchAsyncEnqueuesInteractiveCascadeAboveScanBacklogs() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("33333333-3333-3333-3333-333333333338");
        var episodeId = Guid.Parse("33333333-3333-3333-3333-333333333339");
        SeedProvider(db);
        SeedEntity(db, seriesId, "video-series", "Known Series");
        var episode = SeedEntity(db, episodeId, "video", "Local Episode 1");
        episode.ParentEntityId = seriesId;
        episode.SortOrder = 1;
        await db.SaveChangesAsync();
        var queue = new RecordingJobQueue();
        var service = new IdentifyQueueService(
            db,
            CreateIdentifyService(db, new StructuralChildrenProcessExecutor(), _tempRoot),
            new InMemoryIdentifyApplyProgressStore(),
            queue);
        await service.AddAsync(seriesId, CancellationToken.None);

        var query = new IdentifyQuery(null, null, new Dictionary<string, string> { ["tmdb"] = "series-1" });
        await SearchToCompletionAsync(service, db, seriesId, new IdentifyQueueSearchRequest("tmdb", query), hideNsfw: false, CancellationToken.None);

        var request = Assert.Single(queue.Enqueued, enqueued => enqueued.Type == JobType.IdentifyCascade);
        Assert.Equal(JobType.IdentifyCascade, request.Type);
        Assert.Equal(JobPriorities.InteractiveIdentify, request.Priority);
        Assert.Equal(JobRunLane.ForegroundIdentify, request.Lane);
        Assert.True(request.Priority > JobPriorities.Scan);
    }

    [Fact]
    public async Task RunCascadeDoesNotReviveAQueueItemRemovedBeforeItRuns() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("55555555-5555-5555-5555-555555555550");
        var episode1Id = Guid.Parse("55555555-5555-5555-5555-555555555551");
        var episode2Id = Guid.Parse("55555555-5555-5555-5555-555555555552");
        SeedProvider(db);
        SeedEntity(db, seriesId, "video-series", "Known Series");
        var episode1 = SeedEntity(db, episode1Id, "video", "Local Episode 1");
        episode1.ParentEntityId = seriesId;
        episode1.SortOrder = 1;
        var episode2 = SeedEntity(db, episode2Id, "video", "Local Episode 2");
        episode2.ParentEntityId = seriesId;
        episode2.SortOrder = 2;
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new StructuralChildrenProcessExecutor(), _tempRoot);
        await service.AddAsync(seriesId, CancellationToken.None);

        var query = new IdentifyQuery(null, null, new Dictionary<string, string> { ["tmdb"] = "series-1" });
        await SearchToCompletionAsync(service, db, seriesId, new IdentifyQueueSearchRequest("tmdb", query), hideNsfw: false, CancellationToken.None);
        var cascadeJobId = (await db.IdentifyQueueItems.AsNoTracking().SingleAsync(i => i.EntityId == seriesId)).CascadeJobId!.Value;

        // The user removes the item from the queue before the background cascade gets to run.
        await service.DeleteAsync(seriesId, CancellationToken.None);

        // The now-orphaned cascade runs anyway (job cancellation does not interrupt an in-flight worker).
        // It must drop the walk on the removed parent and never re-populate it with children.
        await service.RunCascadeAsync(
            new IdentifyCascadePayload(seriesId, "tmdb", query, HideNsfw: false),
            cascadeJobId,
            isFinalAttempt: true,
            CancellationToken.None);

        var row = await db.IdentifyQueueItems.AsNoTracking().SingleAsync(i => i.EntityId == seriesId);
        Assert.Equal(IdentifyQueueState.Deleted, row.State);
        var resolved = await service.GetAsync(seriesId, CancellationToken.None);
        Assert.True(resolved?.Proposal is null || resolved.Proposal.Children.Count == 0);
    }

    [Fact]
    public async Task RunCascadeForASupersededSearchDoesNotStreamChildrenOntoTheItem() {
        // Identify with provider A enqueues cascade A. The user goes back to search and re-runs with a
        // different plugin, which enqueues cascade B and stamps the queue item with B's job id. Cascade A
        // is still running in the worker; it must not stream its child tree onto the item now owned by B,
        // otherwise the two cascades overwrite each other by GUID and the children show up duplicated.
        await using var db = CreateContext();
        var seriesId = Guid.Parse("66666666-6666-6666-6666-666666666660");
        var episode1Id = Guid.Parse("66666666-6666-6666-6666-666666666661");
        var episode2Id = Guid.Parse("66666666-6666-6666-6666-666666666662");
        var supersedingJobId = Guid.Parse("66666666-6666-6666-6666-6666666666bb");
        SeedProvider(db);
        SeedEntity(db, seriesId, "video-series", "Known Series");
        var episode1 = SeedEntity(db, episode1Id, "video", "Local Episode 1");
        episode1.ParentEntityId = seriesId;
        episode1.SortOrder = 1;
        var episode2 = SeedEntity(db, episode2Id, "video", "Local Episode 2");
        episode2.ParentEntityId = seriesId;
        episode2.SortOrder = 2;
        // The item is mid-review with the seeded parent proposal (no children yet), already owned by the
        // newer cascade B.
        var seed = new EntityMetadataProposal(
            "tmdb:series:1", "tmdb", ProposalKind.VideoSeries, 1, "external-id",
            EmptyPatch("Known Series identified"), [], [], [], TargetEntityId: seriesId, Relationships: []);
        db.IdentifyQueueItems.Add(new IdentifyQueueItemRow {
            Id = Guid.NewGuid(),
            EntityId = seriesId,
            State = IdentifyQueueState.Proposal,
            ProviderCode = "tmdb",
            Action = IdentifyAction.LookupId,
            ProposalJson = JsonSerializer.Serialize(seed, JsonOptions),
            CascadeJobId = supersedingJobId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new StructuralChildrenProcessExecutor(), _tempRoot);

        // The orphaned cascade A runs with its own (now-stale) job id.
        var query = new IdentifyQuery(null, null, new Dictionary<string, string> { ["tmdb"] = "series-1" });
        await service.RunCascadeAsync(
            new IdentifyCascadePayload(seriesId, "tmdb", query, HideNsfw: false),
            Guid.Parse("66666666-6666-6666-6666-6666666666aa"),
            isFinalAttempt: true,
            CancellationToken.None);

        var resolved = await service.GetAsync(seriesId, CancellationToken.None);
        Assert.NotNull(resolved?.Proposal);
        Assert.Empty(resolved!.Proposal!.Children);
        var row = await db.IdentifyQueueItems.AsNoTracking().SingleAsync(i => i.EntityId == seriesId);
        Assert.Equal(supersedingJobId, row.CascadeJobId);
    }

    [Fact]
    public async Task RunCascadeKeepsTheCascadeMarkerWhenANonFinalAttemptFailsAndClearsItOnTheFinalAttempt() {
        // A cascade attempt that throws must NOT clear the queue item's cascade marker while the job still
        // has retries left, or the review screen would unlock Accept on a half-resolved proposal during the
        // retry gap. The marker is cleared only once the final attempt has failed, so a permanently-failed
        // cascade does not leave Accept disabled forever.
        await using var db = CreateContext();
        var seriesId = Guid.Parse("77777777-7777-7777-7777-777777777770");
        var cascadeJobId = Guid.Parse("77777777-7777-7777-7777-7777777777aa");
        SeedProvider(db);
        SeedEntity(db, seriesId, "video-series", "Known Series");
        var seed = new EntityMetadataProposal(
            "tmdb:series:1", "tmdb", ProposalKind.VideoSeries, 1, "external-id",
            EmptyPatch("Known Series identified"), [], [], [], TargetEntityId: seriesId, Relationships: []);
        db.IdentifyQueueItems.Add(new IdentifyQueueItemRow {
            Id = Guid.NewGuid(),
            EntityId = seriesId,
            State = IdentifyQueueState.Proposal,
            ProviderCode = "tmdb",
            Action = IdentifyAction.LookupId,
            ProposalJson = JsonSerializer.Serialize(seed, JsonOptions),
            CascadeJobId = cascadeJobId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new ThrowingProcessExecutor(), _tempRoot);
        var query = new IdentifyQuery(null, null, new Dictionary<string, string> { ["tmdb"] = "series-1" });
        var payload = new IdentifyCascadePayload(seriesId, "tmdb", query, HideNsfw: false);

        // A retryable failure: the exception propagates, but the marker stays set (cascadeRunning true).
        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.RunCascadeAsync(payload, cascadeJobId, isFinalAttempt: false, CancellationToken.None));
        var afterRetryable = await db.IdentifyQueueItems.AsNoTracking().SingleAsync(i => i.EntityId == seriesId);
        Assert.Equal(cascadeJobId, afterRetryable.CascadeJobId);

        // The final attempt fails: the marker is cleared so Accept is no longer gated forever.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.RunCascadeAsync(payload, cascadeJobId, isFinalAttempt: true, CancellationToken.None));
        var afterFinal = await db.IdentifyQueueItems.AsNoTracking().SingleAsync(i => i.EntityId == seriesId);
        Assert.Null(afterFinal.CascadeJobId);
    }

    [Fact]
    public async Task ApplyAsyncRejectsReapplyingAnAlreadyDoneItem() {
        // A Done row keeps its ProposalJson for history; without the terminal-state guard a re-POST or a
        // bulk-accept loop hitting the same entity twice would re-run the full recursive write.
        await using var db = CreateContext();
        var entityId = Guid.Parse("44444444-4444-4444-4444-444444444445");
        SeedEntity(db, entityId, "video", "Old Title");
        var proposal = Proposal(entityId, "Reviewed Title");
        db.IdentifyQueueItems.Add(new IdentifyQueueItemRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            State = IdentifyQueueState.Done,
            ProviderCode = "tmdb",
            Action = IdentifyAction.LookupId,
            ProposalJson = JsonSerializer.Serialize(proposal, JsonOptions),
            CompletedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new ProposalProcessExecutor(), _tempRoot);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAsync(
                entityId,
                new ApplyIdentifyQueueItemRequest(proposal, ["title"], null),
                CancellationToken.None));

        Assert.Contains("already", error.Message);
        Assert.Equal("Old Title", (await db.Entities.SingleAsync(row => row.Id == entityId)).Title);
    }

    [Fact]
    public async Task ApplyAsyncRejectsApplyingWhileTheCascadeIsStillResolvingChildren() {
        // The stored proposal is only partial until the cascade clears its marker, so applying now would
        // drop the children that have not streamed in yet. The bulk-accept path does not gate on this, so
        // the state machine must reject it.
        await using var db = CreateContext();
        var seriesId = Guid.Parse("44444444-4444-4444-4444-444444444446");
        SeedEntity(db, seriesId, "video-series", "Old Title");
        var proposal = new EntityMetadataProposal(
            "tmdb:series:1", "tmdb", ProposalKind.VideoSeries, 1, "external-id",
            EmptyPatch("Reviewed Title"), [], [], [], TargetEntityId: seriesId, Relationships: []);
        db.IdentifyQueueItems.Add(new IdentifyQueueItemRow {
            Id = Guid.NewGuid(),
            EntityId = seriesId,
            State = IdentifyQueueState.Proposal,
            ProviderCode = "tmdb",
            Action = IdentifyAction.LookupId,
            ProposalJson = JsonSerializer.Serialize(proposal, JsonOptions),
            CascadeJobId = Guid.Parse("44444444-4444-4444-4444-4444444444cc"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new ProposalProcessExecutor(), _tempRoot);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAsync(
                seriesId,
                new ApplyIdentifyQueueItemRequest(proposal, ["title"], null),
                CancellationToken.None));

        Assert.Contains("still resolving", error.Message);
        Assert.Equal("Old Title", (await db.Entities.SingleAsync(row => row.Id == seriesId)).Title);
    }

    [Fact]
    public async Task SearchAsyncKeepsManualTitleSearchInCandidateStateWhenChoiceRequired() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("33333333-3333-3333-3333-333333333334");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Known Movie");
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new ProposalProcessExecutor(), _tempRoot);
        await service.AddAsync(entityId, CancellationToken.None);

        var item = await SearchToCompletionAsync(service, db, 
            entityId,
            new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery("Different Movie", null, null, RequireChoice: true)),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal("search", item.State);
        Assert.Null(item.Proposal);
        var candidate = Assert.Single(item.Candidates);
        Assert.Equal("Known Movie identified", candidate.Title);
        Assert.Equal("123", candidate.ExternalIds["tmdb"]);
        var persisted = await db.IdentifyQueueItems.SingleAsync();
        Assert.Equal(IdentifyQueueState.Search, persisted.State);
        Assert.NotNull(persisted.CandidatesJson);
        Assert.Null(persisted.ProposalJson);
    }

    [Fact]
    public async Task SearchAsyncManualSearchHidesStoredIdentityFromThePlugin() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("33333333-3333-3333-3333-333333333337");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Stored Movie");
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            Provider = "tmdb",
            Value = "123",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new StoredIdLockingProcessExecutor(), _tempRoot);
        await service.AddAsync(entityId, CancellationToken.None);

        var item = await SearchToCompletionAsync(service, db, 
            entityId,
            new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery("Different Movie", null, null, RequireChoice: true)),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal("search", item.State);
        Assert.Null(item.Proposal);
        var candidate = Assert.Single(item.Candidates);
        Assert.Equal("Ambiguous Movie (2005)", candidate.Title);
    }

    [Fact]
    public async Task SearchAsyncWithRequireChoiceIsNotOverriddenByParentCascade() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("33333333-3333-3333-3333-333333333335");
        var episodeId = Guid.Parse("33333333-3333-3333-3333-333333333336");
        SeedProvider(db);
        SeedEntity(db, seriesId, "video-series", "Known Series");
        var episode = SeedEntity(db, episodeId, "video", "Known Episode");
        episode.ParentEntityId = seriesId;
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = seriesId,
            Provider = "tmdb",
            Value = "999",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new SearchCandidatesElseProposalProcessExecutor(), _tempRoot);
        await service.AddAsync(episodeId, CancellationToken.None);

        var item = await SearchToCompletionAsync(service, db, 
            episodeId,
            new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery("Manual Term", null, null, RequireChoice: true)),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal("search", item.State);
        Assert.Null(item.Proposal);
        var candidate = Assert.Single(item.Candidates);
        Assert.Equal("Ambiguous Movie (2005)", candidate.Title);
    }

    [Fact]
    public async Task SearchAsyncWithoutRequireChoiceDoesNotWalkParentChildrenAfterDirectCandidateSearch() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("33333333-3333-3333-3333-33333333333c");
        var episodeId = Guid.Parse("33333333-3333-3333-3333-33333333333d");
        SeedProvider(db);
        SeedEntity(db, seriesId, "video-series", "Known Series");
        var episode = SeedEntity(db, episodeId, "video", "Known Episode");
        episode.ParentEntityId = seriesId;
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = seriesId,
            Provider = "tmdb",
            Value = "series-999",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var executor = new ParentFallbackProcessExecutor(includeMatchingChildInParentCatalog: false);
        var service = CreateQueueService(db, executor, _tempRoot);
        await service.AddAsync(episodeId, CancellationToken.None);

        var item = await SearchToCompletionAsync(service, db,
            episodeId,
            new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery("Known Episode", null, null)),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal("search", item.State);
        Assert.Null(item.Proposal);
        var candidate = Assert.Single(item.Candidates);
        Assert.Equal("Direct candidate", candidate.Title);
        Assert.DoesNotContain(executor.Requests, request =>
            request.Entity.Id == episodeId &&
            string.IsNullOrWhiteSpace(request.Query.Title) &&
            request.StructuralContext?.Ancestors.Any(ancestor => ancestor.Id == seriesId) == true);
    }

    [Fact]
    public async Task SearchAsyncCanResolveFromBoundedParentCatalogFallbackWithoutChildLookups() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("33333333-3333-3333-3333-33333333333e");
        var episodeId = Guid.Parse("33333333-3333-3333-3333-33333333333f");
        SeedProvider(db);
        SeedEntity(db, seriesId, "video-series", "Known Series");
        var episode = SeedEntity(db, episodeId, "video", "Known Episode");
        episode.ParentEntityId = seriesId;
        episode.SortOrder = 2;
        db.EntityExternalIds.Add(new EntityExternalIdRow {
            Id = Guid.NewGuid(),
            EntityId = seriesId,
            Provider = "tmdb",
            Value = "series-999",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var executor = new ParentFallbackProcessExecutor(includeMatchingChildInParentCatalog: true);
        var service = CreateQueueService(db, executor, _tempRoot);
        await service.AddAsync(episodeId, CancellationToken.None);

        var item = await SearchToCompletionAsync(service, db,
            episodeId,
            new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery("Missing Direct Match", null, null)),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal("proposal", item.State);
        Assert.NotNull(item.Proposal);
        Assert.Equal(episodeId, item.Proposal.TargetEntityId);
        Assert.Equal("Known Episode from parent catalog", item.Proposal.Patch.Title);
        Assert.DoesNotContain(executor.Requests, request =>
            request.Entity.Id == episodeId &&
            string.IsNullOrWhiteSpace(request.Query.Title) &&
            request.StructuralContext?.Ancestors.Any(ancestor => ancestor.Id == seriesId) == true);
        Assert.Equal(
            [episodeId, seriesId],
            executor.Requests.Select(request => request.Entity.Id).ToArray());
    }

    [Fact]
    public async Task ApplyAsyncUsesReviewedProposalAndMarksItemDone() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        SeedEntity(db, entityId, "video", "Old Title");
        var proposal = Proposal(entityId, "Reviewed Title");
        db.IdentifyQueueItems.Add(new IdentifyQueueItemRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            State = IdentifyQueueState.Proposal,
            ProviderCode = "tmdb",
            Action = IdentifyAction.LookupId,
            ProposalJson = JsonSerializer.Serialize(proposal, JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new ProposalProcessExecutor(), _tempRoot);

        var applied = await service.ApplyAsync(
            entityId,
            new ApplyIdentifyQueueItemRequest(
                proposal,
                ["title"],
                null),
            CancellationToken.None);

        Assert.Equal("done", applied.State);
        Assert.NotNull(applied.CompletedAt);
        Assert.Equal("Reviewed Title", (await db.Entities.SingleAsync(row => row.Id == entityId)).Title);
        Assert.Empty(await service.ListAsync(includeCompleted: false, hideNsfw: false, CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAsyncRejectsScopedChildProposalForRootQueueItem() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var seasonId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        SeedEntity(db, seriesId, "video-series", "Series");
        var season = SeedEntity(db, seasonId, "video-season", "Season 1");
        season.ParentEntityId = seriesId;
        season.SortOrder = 1;
        var proposal = NsfwTreeProposal(seriesId, seasonId);
        var scopedSeasonProposal = proposal.Children.Single();
        db.IdentifyQueueItems.Add(new IdentifyQueueItemRow {
            Id = Guid.NewGuid(),
            EntityId = seriesId,
            State = IdentifyQueueState.Proposal,
            ProviderCode = "tmdb",
            Action = IdentifyAction.LookupId,
            ProposalJson = JsonSerializer.Serialize(proposal, JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new ProposalProcessExecutor(), _tempRoot);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAsync(
                seriesId,
                new ApplyIdentifyQueueItemRequest(
                    scopedSeasonProposal,
                    ["title"],
                    null),
                CancellationToken.None));

        Assert.Contains("root identify proposal", error.Message);
        Assert.Equal("Series", (await db.Entities.SingleAsync(row => row.Id == seriesId)).Title);
    }

    [Fact]
    public async Task ApplyAsyncMarksFlagsAcrossAcceptedProposalTree() {
        await using var db = CreateContext();
        var seriesId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var seasonId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        SeedEntity(db, seriesId, "video-series", "Series");
        var season = SeedEntity(db, seasonId, "video-season", "Season 1");
        season.ParentEntityId = seriesId;
        season.SortOrder = 1;
        var proposal = NsfwTreeProposal(seriesId, seasonId);
        db.IdentifyQueueItems.Add(new IdentifyQueueItemRow {
            Id = Guid.NewGuid(),
            EntityId = seriesId,
            State = IdentifyQueueState.Proposal,
            ProviderCode = "tmdb",
            Action = IdentifyAction.LookupId,
            ProposalJson = JsonSerializer.Serialize(proposal, JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new ProposalProcessExecutor(), _tempRoot);

        await service.ApplyAsync(
            seriesId,
            new ApplyIdentifyQueueItemRequest(
                proposal,
                ["tags", "credits"],
                null),
            CancellationToken.None);

        var entities = await db.Entities.ToDictionaryAsync(row => row.Id);
        Assert.True(entities[seriesId].IsNsfw);
        Assert.True(entities[seriesId].IsOrganized);
        Assert.True(entities[seasonId].IsNsfw);
        Assert.True(entities[seasonId].IsOrganized);
        var personId = await db.Entities
            .Where(row => row.KindCode == "person" && row.Title == "NSFW Actor")
            .Select(row => row.Id)
            .SingleAsync();
        var tagId = await db.Entities
            .Where(row => row.KindCode == "tag" && row.Title == "NSFW Tag")
            .Select(row => row.Id)
            .SingleAsync();
        Assert.True(entities[personId].IsNsfw);
        Assert.True(entities[personId].IsOrganized);
        Assert.True(entities[tagId].IsNsfw);
        Assert.True(entities[tagId].IsOrganized);
    }

    [Fact]
    public async Task ApplyAsyncMarksAcceptedAudioTrackChildrenOrganized() {
        await using var db = CreateContext();
        var albumId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var trackId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        const string scannedFilePath = "/media/audio/album/01 scanned file.flac";
        SeedEntity(db, albumId, EntityKindRegistry.AudioLibrary.Code, "Scanned Album");
        var track = SeedEntity(db, trackId, EntityKindRegistry.AudioTrack.Code, "01 scanned file");
        track.ParentEntityId = albumId;
        track.SortOrder = 1;
        db.EntityFiles.Add(new EntityFileRow {
            Id = Guid.NewGuid(),
            EntityId = trackId,
            Role = EntityFileRole.Source,
            Path = scannedFilePath,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var proposal = AudioAlbumProposal(albumId, trackId);
        db.IdentifyQueueItems.Add(new IdentifyQueueItemRow {
            Id = Guid.NewGuid(),
            EntityId = albumId,
            State = IdentifyQueueState.Proposal,
            ProviderCode = "tmdb",
            Action = IdentifyAction.LookupId,
            ProposalJson = JsonSerializer.Serialize(proposal, JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new ProposalProcessExecutor(), _tempRoot);

        await service.ApplyAsync(
            albumId,
            new ApplyIdentifyQueueItemRequest(
                proposal,
                ["title"],
                null),
            CancellationToken.None);

        var entities = await db.Entities.ToDictionaryAsync(row => row.Id);
        Assert.Equal("Identified Album", entities[albumId].Title);
        Assert.True(entities[albumId].IsOrganized);
        Assert.Equal("Identified Song", entities[trackId].Title);
        Assert.True(entities[trackId].IsOrganized);

        await new LibraryScanPersistenceService(db).UpsertAudioTrackAsync(
            scannedFilePath,
            "01 scanned file",
            albumId,
            sortOrder: 1,
            sectionLabel: null,
            sectionOrder: 0,
            isNsfw: false,
            CancellationToken.None);

        var rescannedTrack = await db.Entities.SingleAsync(row => row.Id == trackId);
        Assert.Equal("Identified Song", rescannedTrack.Title);
        Assert.True(rescannedTrack.IsOrganized);
    }

    [Fact]
    public async Task ListAsyncHidesNsfwItemsWhenRequestedAndMarksVisibleNsfwRows() {
        await using var db = CreateContext();
        var safeId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        var nsfwId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111");
        SeedEntity(db, safeId, "video", "Safe Movie");
        SeedEntity(db, nsfwId, "video", "NSFW Movie", isNsfw: true);
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new CandidateProcessExecutor(), _tempRoot);
        await service.AddAsync(safeId, CancellationToken.None);
        await service.AddAsync(nsfwId, CancellationToken.None);

        var sfwRows = await service.ListAsync(includeCompleted: false, hideNsfw: true, CancellationToken.None);
        var allRows = await service.ListAsync(includeCompleted: false, hideNsfw: false, CancellationToken.None);

        Assert.Equal([safeId], sfwRows.Select(row => row.EntityId).ToArray());
        Assert.False(Assert.Single(sfwRows).IsNsfw);
        Assert.Equal([safeId, nsfwId], allRows.Select(row => row.EntityId).ToArray());
        Assert.True(allRows.Single(row => row.EntityId == nsfwId).IsNsfw);
    }

    public void Dispose() {
        if (Directory.Exists(_tempRoot)) {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"identify-queue-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }

    /// <summary>
    /// Requests a search and immediately runs its identify-search job to completion, mirroring what
    /// the worker does, so tests can assert on the final persisted outcome.
    /// </summary>
    private static async Task<IdentifyQueueItem> SearchToCompletionAsync(
        IdentifyQueueService service,
        PrismediaDbContext db,
        Guid entityId,
        IdentifyQueueSearchRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        await service.RequestSearchAsync(entityId, request, hideNsfw, cancellationToken);
        var row = await db.IdentifyQueueItems
            .AsNoTracking()
            .FirstAsync(item => item.EntityId == entityId, cancellationToken);
        await service.RunSearchAsync(
            new IdentifySearchPayload(entityId, request.Provider, request.Query, hideNsfw, IsForeground: true),
            row.SearchJobId!.Value,
            isFinalAttempt: true,
            cancellationToken);
        return (await service.GetAsync(entityId, cancellationToken))!;
    }

    [Fact]
    public async Task RequestSearchAsyncQueuesItemAndStampsSearchJob() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("66666666-6666-6666-6666-666666666661");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Queued Movie");
        await db.SaveChangesAsync();
        var queue = new RecordingJobQueue();
        var service = new IdentifyQueueService(
            db,
            CreateIdentifyService(db, new ProposalProcessExecutor(), _tempRoot),
            new InMemoryIdentifyApplyProgressStore(),
            queue);

        var item = await service.RequestSearchAsync(
            entityId, new IdentifyQueueSearchRequest("tmdb", null), hideNsfw: false, CancellationToken.None);

        Assert.Equal("queued", item.State);
        Assert.Equal("tmdb", item.Provider);
        var job = Assert.Single(queue.Enqueued);
        Assert.Equal(JobType.IdentifySearch, job.Type);
        Assert.Equal(entityId.ToString(), job.TargetEntityId);
        Assert.Equal(JobPriorities.InteractiveIdentify, job.Priority);
        Assert.Equal(JobRunLane.ForegroundIdentify, job.Lane);
        Assert.True(IdentifySearchPayload.Parse(job.PayloadJson!).IsForeground);
        var row = await db.IdentifyQueueItems.AsNoTracking().SingleAsync();
        Assert.Equal(IdentifyQueueState.Queued, row.State);
        Assert.NotNull(row.SearchJobId);
    }

    [Fact]
    public async Task RequestSearchBatchAsyncDoesNotUseForegroundIdentifyLane() {
        await using var db = CreateContext();
        var firstId = Guid.Parse("66666666-6666-6666-6666-6666666666a1");
        var secondId = Guid.Parse("66666666-6666-6666-6666-6666666666a2");
        SeedProvider(db);
        SeedEntity(db, firstId, "video", "Batch Movie 1");
        SeedEntity(db, secondId, "video", "Batch Movie 2");
        await db.SaveChangesAsync();
        var queue = new RecordingJobQueue();
        var service = new IdentifyQueueService(
            db,
            CreateIdentifyService(db, new ProposalProcessExecutor(), _tempRoot),
            new InMemoryIdentifyApplyProgressStore(),
            queue);

        var response = await service.RequestSearchBatchAsync(
            [firstId, secondId],
            new IdentifyQueueSearchRequest("tmdb", null),
            hideNsfw: false,
            CancellationToken.None);

        Assert.Equal(2, response.Enqueued);
        Assert.All(queue.Enqueued, job => {
            Assert.Equal(JobType.IdentifySearch, job.Type);
            Assert.Equal(JobPriorities.InteractiveIdentify, job.Priority);
            Assert.Null(job.Lane);
            Assert.False(IdentifySearchPayload.Parse(job.PayloadJson!).IsForeground);
        });
    }

    [Fact]
    public async Task RequestSearchAsyncSupersedesAPendingSearch() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("66666666-6666-6666-6666-666666666662");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Superseded Movie");
        await db.SaveChangesAsync();
        var queue = new RecordingJobQueue();
        var service = new IdentifyQueueService(
            db,
            CreateIdentifyService(db, new ProposalProcessExecutor(), _tempRoot),
            new InMemoryIdentifyApplyProgressStore(),
            queue);

        await service.RequestSearchAsync(entityId, new IdentifyQueueSearchRequest("tmdb", null), false, CancellationToken.None);
        var firstJobId = (await db.IdentifyQueueItems.AsNoTracking().SingleAsync()).SearchJobId!.Value;

        await service.RequestSearchAsync(
            entityId,
            new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery("Other", null, null)),
            false,
            CancellationToken.None);

        var row = await db.IdentifyQueueItems.AsNoTracking().SingleAsync();
        Assert.NotEqual(firstJobId, row.SearchJobId);
        // The first job was cancelled, so only the new one is still pending.
        var pending = Assert.Single(queue.Pending);
        Assert.Equal(row.SearchJobId, pending.Id);
    }

    [Fact]
    public async Task RunSearchAsyncWithStaleMarkerLeavesTheItemUntouched() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("66666666-6666-6666-6666-666666666663");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Fenced Movie");
        await db.SaveChangesAsync();
        var queue = new RecordingJobQueue();
        var service = new IdentifyQueueService(
            db,
            CreateIdentifyService(db, new ProposalProcessExecutor(), _tempRoot),
            new InMemoryIdentifyApplyProgressStore(),
            queue);
        await service.RequestSearchAsync(entityId, new IdentifyQueueSearchRequest("tmdb", null), false, CancellationToken.None);

        var staleJobId = Guid.NewGuid();
        await service.RunSearchAsync(
            new IdentifySearchPayload(entityId, "tmdb", null, false), staleJobId, isFinalAttempt: true, CancellationToken.None);

        var row = await db.IdentifyQueueItems.AsNoTracking().SingleAsync();
        Assert.Equal(IdentifyQueueState.Queued, row.State);
        Assert.Null(row.ProposalJson);
    }

    [Fact]
    public async Task RunSearchAsyncDefersWhenProviderIsRateLimited() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("66666666-6666-6666-6666-666666666664");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Throttled Movie");
        await db.SaveChangesAsync();
        var queue = new RecordingJobQueue();
        var service = new IdentifyQueueService(
            db,
            CreateIdentifyService(db, new RateLimitedProcessExecutor(), _tempRoot),
            new InMemoryIdentifyApplyProgressStore(),
            queue);
        await service.RequestSearchAsync(entityId, new IdentifyQueueSearchRequest("tmdb", null), false, CancellationToken.None);
        var searchJobId = (await db.IdentifyQueueItems.AsNoTracking().SingleAsync()).SearchJobId!.Value;

        var retry = await Assert.ThrowsAsync<JobRetryLaterException>(() =>
            service.RunSearchAsync(
                new IdentifySearchPayload(entityId, "tmdb", null, false), searchJobId, isFinalAttempt: false, CancellationToken.None));

        Assert.Contains("temporarily unavailable", retry.Message);
        var row = await db.IdentifyQueueItems.AsNoTracking().SingleAsync();
        // Back to queued with the marker kept, so the deferred job's retry still owns the search.
        Assert.Equal(IdentifyQueueState.Queued, row.State);
        Assert.Equal(searchJobId, row.SearchJobId);
    }

    [Fact]
    public async Task RunSearchAsyncWalksEnabledProvidersWhenNoneRequested() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("66666666-6666-6666-6666-666666666665");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Walked Movie");
        await db.SaveChangesAsync();
        var queue = new RecordingJobQueue();
        var service = new IdentifyQueueService(
            db,
            CreateIdentifyService(db, new ProposalProcessExecutor(), _tempRoot),
            new InMemoryIdentifyApplyProgressStore(),
            queue);
        await service.RequestSearchAsync(entityId, new IdentifyQueueSearchRequest(null, null), false, CancellationToken.None);
        var searchJobId = (await db.IdentifyQueueItems.AsNoTracking().SingleAsync()).SearchJobId!.Value;

        await service.RunSearchAsync(
            new IdentifySearchPayload(entityId, null, null, false), searchJobId, isFinalAttempt: true, CancellationToken.None);

        var item = (await service.GetAsync(entityId, CancellationToken.None))!;
        Assert.Equal("proposal", item.State);
        Assert.Equal("tmdb", item.Provider);
        var row = await db.IdentifyQueueItems.AsNoTracking().SingleAsync();
        Assert.Null(row.SearchJobId);
    }

    [Fact]
    public async Task ApplyAsyncRejectsItemAwaitingItsSearch() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Pending Movie");
        await db.SaveChangesAsync();
        var queue = new RecordingJobQueue();
        var service = new IdentifyQueueService(
            db,
            CreateIdentifyService(db, new ProposalProcessExecutor(), _tempRoot),
            new InMemoryIdentifyApplyProgressStore(),
            queue);
        await service.RequestSearchAsync(entityId, new IdentifyQueueSearchRequest("tmdb", null), false, CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAsync(entityId, new ApplyIdentifyQueueItemRequest(null, ["title"], null), CancellationToken.None));

        Assert.Contains("awaiting its requested search", error.Message);
    }

    [Fact]
    public async Task DeleteAsyncCancelsThePendingSearchJob() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("66666666-6666-6666-6666-666666666667");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Rejected Movie");
        await db.SaveChangesAsync();
        var queue = new RecordingJobQueue();
        var service = new IdentifyQueueService(
            db,
            CreateIdentifyService(db, new ProposalProcessExecutor(), _tempRoot),
            new InMemoryIdentifyApplyProgressStore(),
            queue);
        await service.RequestSearchAsync(entityId, new IdentifyQueueSearchRequest("tmdb", null), false, CancellationToken.None);

        var item = await service.DeleteAsync(entityId, CancellationToken.None);

        Assert.Equal("deleted", item!.State);
        Assert.Empty(queue.Pending);
        var row = await db.IdentifyQueueItems.AsNoTracking().SingleAsync();
        Assert.Null(row.SearchJobId);
    }

    [Fact]
    public async Task GetAsyncReconcilesAnOrphanedQueuedItem() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("66666666-6666-6666-6666-666666666668");
        SeedEntity(db, entityId, "video", "Orphaned Movie");
        // A backfilled or abandoned row: queued, but no identify-search job owns it.
        db.IdentifyQueueItems.Add(new IdentifyQueueItemRow {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            State = IdentifyQueueState.Queued,
            Action = IdentifyAction.Search,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new ProposalProcessExecutor(), _tempRoot);

        var item = await service.GetAsync(entityId, CancellationToken.None);

        Assert.Equal("error", item!.State);
        Assert.Contains("no longer running", item.Error);
        var row = await db.IdentifyQueueItems.AsNoTracking().SingleAsync();
        Assert.Equal(IdentifyQueueState.Error, row.State);
    }

    private static IdentifyQueueService CreateQueueService(
        PrismediaDbContext db,
        ProcessExecutor executor,
        string tempRoot) =>
        new(db, CreateIdentifyService(db, executor, tempRoot), new InMemoryIdentifyApplyProgressStore(), new RecordingJobQueue());

    private static IdentifyPluginService CreateIdentifyService(
        PrismediaDbContext db,
        ProcessExecutor executor,
        string tempRoot) {
        WriteManifest(tempRoot);
        return new IdentifyPluginService(
            db,
            new PluginCatalogService(db, new PluginCatalogOptions([tempRoot], tempRoot, "1.0.0")),
            new IdentifyMatchHintResolver(db),
            new IdentifyRunnerSelector([new DotnetPluginProcessRunner(executor, new PluginCatalogOptions([], tempRoot, "1.0.0"))]),
            new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(tempRoot)));
    }

    /// <summary>Minimal in-memory job queue for tests: records enqueues, no worker runs the cascade.</summary>
    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];
        public List<JobRunSnapshot> Pending { get; } = [];

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Enqueued.Add(request);
            var snapshot = new JobRunSnapshot(
                Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null,
                request.PayloadJson ?? string.Empty, request.TargetEntityKind, request.TargetEntityId,
                request.TargetLabel, DateTimeOffset.UtcNow, null, null);
            Pending.Add(snapshot);
            return Task.FromResult(snapshot);
        }

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            EnqueueAsync(new EnqueueJobRequest(type), cancellationToken);

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(Pending.Any(job => job.Type == type && job.TargetEntityId == targetEntityId));
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) {
            Pending.RemoveAll(job => job.Id == id);
            return Task.FromResult(true);
        }
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) => Task.FromResult<JobRunSnapshot?>(null);
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobQueueCount>>([]);
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private static void WriteManifest(string root) {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "manifest.json"),
            """
            {
              "manifestVersion": 1,
              "apiTags": ["prismedia"],
              "id": "tmdb",
              "name": "TMDB",
              "version": "1.0.0",
              "runtime": "dotnet-process",
              "entry": "Prismedia.Plugin.Tmdb.dll",
              "compat": {
                "pluginApiMin": "1.0.0",
                "pluginApiMax": null,
                "prismediaMin": "1.0.0",
                "prismediaMax": null
              },
              "auth": [
                { "key": "apiKey", "label": "API key", "required": true }
              ],
              "supports": [
                { "entityKind": "video", "actions": ["lookup-id", "lookup-url", "search"] },
                { "entityKind": "video-series", "actions": ["lookup-id", "lookup-url", "search"] }
              ]
            }
            """);
    }

    private static void SeedProvider(PrismediaDbContext db) {
        var now = DateTimeOffset.UtcNow;
        var providerConfig = new ProviderConfigRow {
            Id = Guid.NewGuid(),
            ProviderCode = "tmdb",
            DisplayName = "TMDB",
            ProviderType = ProviderType.ExternalProcess,
            Enabled = true,
            SettingsJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ProviderConfigs.Add(providerConfig);
        db.ProviderCredentials.Add(new ProviderCredentialRow {
            Id = Guid.NewGuid(),
            ProviderConfigId = providerConfig.Id,
            CredentialKey = "apiKey",
            EncryptedValue = "secret",
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static EntityRow SeedEntity(PrismediaDbContext db, Guid id, string kind, string title, bool isNsfw = false) {
        var entity = new EntityRow {
            Id = id,
            KindCode = kind,
            Title = title,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        entity.IsNsfw = isNsfw;
        db.Entities.Add(entity);

        return entity;
    }

    private static EntityMetadataProposal Proposal(Guid entityId, string title) =>
        new(
            "tmdb:123",
            "tmdb",
            ProposalKind.Video,
            1,
            "external-id",
            new EntityMetadataPatch(
                title,
                null,
                new Dictionary<string, string> { ["tmdb"] = "123" },
                [],
                [],
                null,
                [],
                new Dictionary<string, string>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                null),
            [],
            [],
            [],
            TargetEntityId: entityId,
            Relationships: []);

    private static EntityMetadataProposal NsfwTreeProposal(Guid seriesId, Guid seasonId) {
        var person = new EntityMetadataProposal(
            "tmdb:person:nsfw",
            "tmdb",
            ProposalKind.Person,
            1,
            "credit",
            EmptyPatch("NSFW Actor") with { Flags = new EntityMetadataFlagsPatch(null, true, null) },
            [],
            [],
            [],
            Relationships: []);
        var tag = new EntityMetadataProposal(
            "tmdb:tag:nsfw",
            "tmdb",
            ProposalKind.Tag,
            1,
            "tag",
            EmptyPatch("NSFW Tag") with { Flags = new EntityMetadataFlagsPatch(null, true, null) },
            [],
            [],
            [],
            Relationships: []);
        var season = new EntityMetadataProposal(
            "tmdb:season:1",
            "tmdb",
            ProposalKind.VideoSeason,
            1,
            "cascade",
            EmptyPatch("Season 1") with { Flags = new EntityMetadataFlagsPatch(null, true, null) },
            [],
            [],
            [],
            TargetEntityId: seasonId,
            Relationships: []);

        return new EntityMetadataProposal(
            "tmdb:series:1",
            "tmdb",
            ProposalKind.VideoSeries,
            1,
            "external-id",
            EmptyPatch("Series") with {
                Tags = ["NSFW Tag"],
                Credits = [new CreditPatch("NSFW Actor", "cast", null, 0)],
                Flags = new EntityMetadataFlagsPatch(null, true, null)
            },
            [],
            [season],
            [],
            TargetEntityId: seriesId,
            Relationships: [person, tag]);
    }

    private static EntityMetadataProposal AudioAlbumProposal(Guid albumId, Guid trackId) {
        var track = new EntityMetadataProposal(
            "tmdb:track:1",
            "tmdb",
            ProposalKind.AudioTrack,
            1,
            "cascade",
            EmptyPatch("Identified Song"),
            [],
            [],
            [],
            TargetEntityId: trackId,
            Relationships: []);

        return new EntityMetadataProposal(
            "tmdb:album:1",
            "tmdb",
            ProposalKind.AudioLibrary,
            1,
            "external-id",
            EmptyPatch("Identified Album"),
            [],
            [track],
            [],
            TargetEntityId: albumId,
            Relationships: []);
    }

    private static EntityMetadataPatch EmptyPatch(string? title) =>
        new(
            title,
            null,
            new Dictionary<string, string>(),
            [],
            [],
            null,
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            null);

    private static string SerializeWireProposal(Guid entityId, string title) =>
        JsonSerializer.Serialize(
            new {
                ok = true,
                result = new {
                    type = "proposal",
                    proposal = Proposal(entityId, title),
                    candidates = Array.Empty<object>()
                },
                error = (string?)null
            },
            JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = true,
        // Mirror the plugin wire / stored-proposal format: codec enums round-trip as their code.
        Converters = { new CodecJsonConverterFactory() }
    };

    /// <summary>A plugin process that always crashes, to simulate a cascade attempt throwing mid-walk.</summary>
    private sealed class RateLimitedProcessExecutor : ProcessExecutor {
        public override Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var wire = new {
                ok = false,
                result = (object?)null,
                error = "429 Too Many Requests"
            };
            return Task.FromResult(new ProcessExecutionResult(0, JsonSerializer.Serialize(wire, JsonOptions), string.Empty));
        }
    }

    private sealed class ThrowingProcessExecutor : ProcessExecutor {
        public override Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) =>
            throw new InvalidOperationException("Plugin process crashed.");
    }

    private sealed class CandidateProcessExecutor : ProcessExecutor {
        public override Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var wire = new {
                ok = true,
                result = new {
                    type = "candidates",
                    proposal = (object?)null,
                    candidates = new[] {
                        new EntitySearchCandidate(
                            new Dictionary<string, string> { ["tmdb"] = "2005" },
                            "Ambiguous Movie (2005)",
                            2005,
                            "A search result that still needs user confirmation.",
                            "https://example.test/poster.jpg",
                            9.1m)
                    }
                },
                error = (string?)null
            };

            return Task.FromResult(new ProcessExecutionResult(0, JsonSerializer.Serialize(wire, JsonOptions), string.Empty));
        }
    }

    private sealed class ProposalProcessExecutor : ProcessExecutor {
        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(requestJson, JsonOptions)!;
            return new ProcessExecutionResult(
                0,
                SerializeWireProposal(request.Entity.Id, $"{request.Entity.Title} identified"),
                string.Empty);
        }
    }

    /// <summary>
    /// Mimics a plugin that reaches for any stored provider id it can see (query, entity
    /// snapshot, or hints) before honoring the requested action — the lock-in shape that
    /// hijacks a manual search back to the saved match.
    /// </summary>
    private sealed class StoredIdLockingProcessExecutor : ProcessExecutor {
        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(requestJson, JsonOptions)!;
            var storedId = request.Query.ExternalIds?.GetValueOrDefault("tmdb")
                ?? request.Entity.ExternalIds?.GetValueOrDefault("tmdb")
                ?? request.Hints.ExternalIds.GetValueOrDefault("tmdb");
            if (storedId is not null) {
                return new ProcessExecutionResult(
                    0,
                    SerializeWireProposal(request.Entity.Id, $"{request.Entity.Title} identified"),
                    string.Empty);
            }

            var wire = new {
                ok = true,
                result = new {
                    type = "candidates",
                    proposal = (object?)null,
                    candidates = new[] {
                        new EntitySearchCandidate(
                            new Dictionary<string, string> { ["tmdb"] = "2005" },
                            "Ambiguous Movie (2005)",
                            2005,
                            "A search result that still needs user confirmation.",
                            "https://example.test/poster.jpg",
                            9.1m)
                    }
                },
                error = (string?)null
            };
            return new ProcessExecutionResult(0, JsonSerializer.Serialize(wire, JsonOptions), string.Empty);
        }
    }

    /// <summary>
    /// Returns search candidates for explicit search actions and a confident proposal for
    /// everything else — the shape that lets a child's manual search collide with its
    /// parent's stored-id auto match.
    /// </summary>
    private sealed class SearchCandidatesElseProposalProcessExecutor : ProcessExecutor {
        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(requestJson, JsonOptions)!;
            if (request.Action == IdentifyAction.Search) {
                var wire = new {
                    ok = true,
                    result = new {
                        type = "candidates",
                        proposal = (object?)null,
                        candidates = new[] {
                            new EntitySearchCandidate(
                                new Dictionary<string, string> { ["tmdb"] = "2005" },
                                "Ambiguous Movie (2005)",
                                2005,
                                "A search result that still needs user confirmation.",
                                "https://example.test/poster.jpg",
                                9.1m)
                        }
                    },
                    error = (string?)null
                };
                return new ProcessExecutionResult(0, JsonSerializer.Serialize(wire, JsonOptions), string.Empty);
            }

            return new ProcessExecutionResult(
                0,
                SerializeWireProposal(request.Entity.Id, $"{request.Entity.Title} identified"),
                string.Empty);
        }
    }

    private sealed class ParentFallbackProcessExecutor(bool includeMatchingChildInParentCatalog) : ProcessExecutor {
        public List<IdentifyPluginRequest> Requests { get; } = [];

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(requestJson, JsonOptions)!;
            Requests.Add(request);

            if (request.Entity.Kind == EntityKind.VideoSeries) {
                var children = includeMatchingChildInParentCatalog
                    ? new[] {
                        new EntityMetadataProposal(
                            "tmdb:series-999:episode:2",
                            "tmdb",
                            ProposalKind.Video,
                            0.98m,
                            "parent-catalog",
                            EmptyPatch("Known Episode from parent catalog") with {
                                ExternalIds = new Dictionary<string, string> { ["tmdb"] = "episode-2" },
                                Positions = new Dictionary<string, int> { ["sortOrder"] = 2 }
                            },
                            [],
                            [],
                            [],
                            Relationships: [])
                    }
                    : [];
                return ProposalResponse(new EntityMetadataProposal(
                    "tmdb:series-999",
                    "tmdb",
                    ProposalKind.VideoSeries,
                    1m,
                    "external-id",
                    EmptyPatch("Known Series identified") with {
                        ExternalIds = new Dictionary<string, string> { ["tmdb"] = "series-999" }
                    },
                    [],
                    children,
                    [],
                    TargetEntityId: request.Entity.Id,
                    Relationships: []));
            }

            if (request.Query.Title == "Missing Direct Match") {
                var none = new {
                    ok = true,
                    result = new { type = "none", proposal = (object?)null, candidates = Array.Empty<object>() },
                    error = (string?)null
                };
                return new ProcessExecutionResult(0, JsonSerializer.Serialize(none, JsonOptions), string.Empty);
            }

            if (string.IsNullOrWhiteSpace(request.Query.Title) &&
                request.StructuralContext?.Ancestors.Any() == true) {
                return ProposalResponse(Proposal(request.Entity.Id, "Unexpected child lookup"));
            }

            var wire = new {
                ok = true,
                result = new {
                    type = "candidates",
                    proposal = (object?)null,
                    candidates = new[] {
                        new EntitySearchCandidate(
                            new Dictionary<string, string> { ["tmdb"] = "direct-1" },
                            "Direct candidate",
                            2026,
                            "Direct search candidate; the host must not override it by walking siblings.",
                            null,
                            0.5m)
                    }
                },
                error = (string?)null
            };
            return new ProcessExecutionResult(0, JsonSerializer.Serialize(wire, JsonOptions), string.Empty);
        }

        private static ProcessExecutionResult ProposalResponse(EntityMetadataProposal proposal) {
            var wire = new {
                ok = true,
                result = new {
                    type = "proposal",
                    proposal,
                    candidates = Array.Empty<object>()
                },
                error = (string?)null
            };
            return new ProcessExecutionResult(0, JsonSerializer.Serialize(wire, JsonOptions), string.Empty);
        }
    }

    private sealed class StructuralChildrenProcessExecutor : ProcessExecutor {
        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(requestJson, JsonOptions)!;
            var proposal = new EntityMetadataProposal(
                "tmdb:series:1",
                "tmdb",
                ProposalKind.VideoSeries,
                1,
                "external-id",
                EmptyPatch("Known Series identified"),
                [],
                [
                    EpisodeProposal(1),
                    EpisodeProposal(2),
                    EpisodeProposal(3)
                ],
                [],
                TargetEntityId: request.Entity.Id,
                Relationships: []);

            var wire = new {
                ok = true,
                result = new {
                    type = "proposal",
                    proposal,
                    candidates = Array.Empty<object>()
                },
                error = (string?)null
            };
            return new ProcessExecutionResult(0, JsonSerializer.Serialize(wire, JsonOptions), string.Empty);
        }

        private static EntityMetadataProposal EpisodeProposal(int episodeNumber) =>
            new(
                $"tmdb:series:1:episode:{episodeNumber}",
                "tmdb",
                ProposalKind.Video,
                0.9m,
                "cascade",
                EmptyPatch($"Episode {episodeNumber}") with {
                    Positions = new Dictionary<string, int> { ["episodeNumber"] = episodeNumber }
                },
                [],
                [],
                [],
                Relationships: []);
    }

    private sealed class CanonicalCandidateProcessExecutor : ProcessExecutor {
        public override Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var wire = new {
                ok = true,
                result = new {
                    type = "candidates",
                    proposal = (object?)null,
                    candidates = new[] {
                        new {
                            externalIds = new Dictionary<string, string> { ["tmdb"] = "987" },
                            title = "Candidate Movie",
                            overview = "Candidate overview from the provider.",
                            posterUrl = "https://image.example.test/poster.jpg",
                            year = 2025,
                            popularity = 8.75m
                        }
                    }
                },
                error = (string?)null
            };

            return Task.FromResult(new ProcessExecutionResult(0, JsonSerializer.Serialize(wire, JsonOptions), string.Empty));
        }
    }

    /// <summary>
    /// Mimics a provider whose id lookup yields nothing (throttled, or never implemented for the kind)
    /// while its title search still matches — the shape that regressed re-identify once an id was stored.
    /// Records each requested action so a test can assert the lookup-id → search fallback fired.
    /// </summary>
    private sealed class LookupMissSearchHitProcessExecutor : ProcessExecutor {
        public List<string> Actions { get; } = [];

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(requestJson, JsonOptions)!;
            Actions.Add(request.Action.ToCode());

            if (request.Action == IdentifyAction.Search) {
                return new ProcessExecutionResult(
                    0,
                    SerializeWireProposal(request.Entity.Id, $"{request.Entity.Title} via search"),
                    string.Empty);
            }

            var none = new {
                ok = true,
                result = new { type = "none", proposal = (object?)null, candidates = Array.Empty<object>() },
                error = (string?)null
            };
            return new ProcessExecutionResult(0, JsonSerializer.Serialize(none, JsonOptions), string.Empty);
        }
    }

    private sealed class CandidateThenProposalProcessExecutor : ProcessExecutor {
        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(requestJson, JsonOptions)!;
            if (request.Action == IdentifyAction.LookupId) {
                return new ProcessExecutionResult(
                    0,
                    SerializeWireProposal(request.Entity.Id, "Auto-resolved title"),
                    string.Empty);
            }

            var wire = new {
                ok = true,
                result = new {
                    type = "candidates",
                    proposal = (object?)null,
                    candidates = new[] {
                        new EntitySearchCandidate(
                            new Dictionary<string, string> { ["tmdb"] = "2005" },
                            "Ambiguous Movie (2005)",
                            2005,
                            "A search result that still needs user confirmation.",
                            "https://example.test/poster.jpg",
                            9.1m)
                    }
                },
                error = (string?)null
            };

            return new ProcessExecutionResult(0, JsonSerializer.Serialize(wire, JsonOptions), string.Empty);
        }
    }

    private sealed class CandidateThenLookupMissProcessExecutor : ProcessExecutor {
        public List<string> Actions { get; } = [];

        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken, bool lowPriority = false) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(requestJson, JsonOptions)!;
            Actions.Add(request.Action.ToCode());

            if (request.Action == IdentifyAction.LookupId) {
                var none = new {
                    ok = true,
                    result = new { type = "none", proposal = (object?)null, candidates = Array.Empty<object>() },
                    error = (string?)null
                };
                return new ProcessExecutionResult(0, JsonSerializer.Serialize(none, JsonOptions), string.Empty);
            }

            var wire = new {
                ok = true,
                result = new {
                    type = "candidates",
                    proposal = (object?)null,
                    candidates = new[] {
                        new EntitySearchCandidate(
                            new Dictionary<string, string> { ["tmdb"] = "2005" },
                            "Ambiguous Movie (2005)",
                            2005,
                            "A search result that still needs user confirmation.",
                            "https://example.test/poster.jpg",
                            9.1m)
                    }
                },
                error = (string?)null
            };

            return new ProcessExecutionResult(0, JsonSerializer.Serialize(wire, JsonOptions), string.Empty);
        }
    }
}
