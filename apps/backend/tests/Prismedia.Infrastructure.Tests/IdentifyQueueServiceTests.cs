using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Processes;

namespace Prismedia.Infrastructure.Tests;

public sealed class IdentifyQueueServiceTests : IDisposable {
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"prismedia-identify-queue-{Guid.NewGuid():N}");

    [Fact]
    public async Task AddAsyncCreatesDurableSearchItemForEntity() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        SeedEntity(db, entityId, "video-series", "Mystery Show");
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new CandidateProcessExecutor(), _tempRoot);

        var item = await service.AddAsync(entityId, CancellationToken.None);

        Assert.Equal(entityId, item.EntityId);
        Assert.Equal("video-series", item.EntityKind);
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

        var item = await service.SearchAsync(entityId, new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery("Ambiguous", null, null)), hideNsfw: false, CancellationToken.None);

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
    public async Task SearchAsyncNormalizesLegacyProviderCandidateFields() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("22222222-2222-2222-2222-222222222223");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Legacy Candidate Movie");
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new LegacyCandidateProcessExecutor(), _tempRoot);
        await service.AddAsync(entityId, CancellationToken.None);

        var item = await service.SearchAsync(entityId, new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery("Legacy Candidate", null, null)), hideNsfw: false, CancellationToken.None);

        var candidate = Assert.Single(item.Candidates);
        Assert.Equal("Legacy Candidate Movie", candidate.Title);
        Assert.Equal("Description from an older plugin contract.", candidate.Overview);
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

        var item = await service.SearchAsync(entityId, new IdentifyQueueSearchRequest("tmdb", new IdentifyQuery(null, null, new Dictionary<string, string> { ["tmdb"] = "123" })), hideNsfw: false, CancellationToken.None);

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
    public async Task SearchAsyncKeepsManualTitleSearchInCandidateStateWhenChoiceRequired() {
        await using var db = CreateContext();
        var entityId = Guid.Parse("33333333-3333-3333-3333-333333333334");
        SeedProvider(db);
        SeedEntity(db, entityId, "video", "Known Movie");
        await db.SaveChangesAsync();
        var service = CreateQueueService(db, new ProposalProcessExecutor(), _tempRoot);
        await service.AddAsync(entityId, CancellationToken.None);

        var item = await service.SearchAsync(
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
            Action = "lookup-id",
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
            Action = "lookup-id",
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

    private static IdentifyQueueService CreateQueueService(
        PrismediaDbContext db,
        ProcessExecutor executor,
        string tempRoot) {
        WriteManifest(tempRoot);
        var identify = new IdentifyPluginService(
            db,
            new PluginCatalogService(db, new PluginCatalogOptions([tempRoot], tempRoot, "1.0.0")),
            new IdentifyMatchHintResolver(db),
            new DotnetPluginProcessRunner(executor, new PluginCatalogOptions([], tempRoot, "1.0.0")),
            new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(tempRoot)));

        return new IdentifyQueueService(db, identify);
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
            "video",
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
            "person",
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
            "tag",
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
            "video-season",
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
            "video-series",
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
        PropertyNameCaseInsensitive = true
    };

    private sealed class CandidateProcessExecutor : ProcessExecutor {
        public override Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken) {
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
            CancellationToken cancellationToken) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(requestJson, JsonOptions)!;
            return new ProcessExecutionResult(
                0,
                SerializeWireProposal(request.Entity.Id, $"{request.Entity.Title} identified"),
                string.Empty);
        }
    }

    private sealed class LegacyCandidateProcessExecutor : ProcessExecutor {
        public override Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken) {
            var wire = new {
                ok = true,
                result = new {
                    type = "candidates",
                    proposal = (object?)null,
                    candidates = new[] {
                        new {
                            candidateId = "tmdb:movie:987",
                            externalIds = new Dictionary<string, string> { ["tmdb"] = "987" },
                            title = "Legacy Candidate Movie",
                            description = "Description from an older plugin contract.",
                            thumbnailUrl = "https://image.example.test/poster.jpg",
                            year = 2025,
                            source = "TMDB",
                            confidence = 8.75m,
                            matchReason = "title-search"
                        }
                    }
                },
                error = (string?)null
            };

            return Task.FromResult(new ProcessExecutionResult(0, JsonSerializer.Serialize(wire, JsonOptions), string.Empty));
        }
    }

    private sealed class CandidateThenProposalProcessExecutor : ProcessExecutor {
        public override async Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken) {
            var requestJson = await File.ReadAllTextAsync(arguments[1], cancellationToken);
            var request = JsonSerializer.Deserialize<IdentifyPluginRequest>(requestJson, JsonOptions)!;
            if (request.Action == "lookup-id") {
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
}
