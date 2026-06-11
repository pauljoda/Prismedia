using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Plugins;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed class AutoIdentifyRunnerTests {
    [Fact]
    public async Task AppliesFirstConfidentProviderWithFullFieldsAndMarksOrganized() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1", "p2"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["p2"] = Proposal("p2", confidence: 0.95m, title: "The Matrix"),
            },
        };
        var runner = new AutoIdentifyRunner(settings, identify, db, NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("p2", result.Provider);
        var applyCall = Assert.Single(identify.ApplyCalls);
        Assert.Contains("title", applyCall.Fields);
        Assert.Contains("images", applyCall.Fields);
        Assert.DoesNotContain("rating", applyCall.Fields);
        Assert.Equal("poster", Assert.Single(applyCall.SelectedImages!).Key);
        Assert.True((await db.Entities.SingleAsync()).IsOrganized);
    }

    [Fact]
    public async Task SkipsProvidersBelowConfidenceThreshold() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["p1"] = Proposal("p1", confidence: 0.5m, title: "Maybe Match"),
            },
        };
        var runner = new AutoIdentifyRunner(settings, identify, db, NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Empty(identify.ApplyCalls);
        Assert.False((await db.Entities.SingleAsync()).IsOrganized);
    }

    [Fact]
    public async Task TreatsConfidenceFreeResultAsExactMatch() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["p1"] = Proposal("p1", confidence: null, title: "Exact Lookup"),
            },
        };
        var runner = new AutoIdentifyRunner(settings, identify, db, NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Single(identify.ApplyCalls);
    }

    [Fact]
    public async Task HydratesAndAppliesSingleConfidentSearchCandidate() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false, kind: "video-series", title: "The Chair Company");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["tmdb"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["tmdb"] = CandidateShell("tmdb", "271267", "The Chair Company", confidence: 1m),
            },
            ProposalsByExternalId = {
                ["tmdb:271267"] = Proposal("tmdb", confidence: 1m, title: "The Chair Company", targetKind: ProposalKind.VideoSeries),
            },
        };
        var runner = new AutoIdentifyRunner(settings, identify, db, NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("tmdb", result.Provider);
        Assert.Equal(2, identify.IdentifyCalls.Count);
        Assert.Equal("271267", identify.IdentifyCalls[1].Query?.ExternalIds?["tmdb"]);
        Assert.Single(identify.ApplyCalls);
    }

    [Fact]
    public async Task LeavesSearchCandidatesForReviewWhenCandidateConfidenceIsBelowThreshold() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false, kind: "video-series", title: "The Chair Company");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["tmdb"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["tmdb"] = CandidateShell("tmdb", "271267", "The Chair Company", confidence: 0.5m),
            },
            ProposalsByExternalId = {
                ["tmdb:271267"] = Proposal("tmdb", confidence: 1m, title: "The Chair Company", targetKind: ProposalKind.VideoSeries),
            },
        };
        var runner = new AutoIdentifyRunner(settings, identify, db, NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Single(identify.IdentifyCalls);
        Assert.Empty(identify.ApplyCalls);
    }

    [Fact]
    public async Task SkipsOrganizedEntityWhenUnorganizedOnly() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: true);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = { ["p1"] = Proposal("p1", confidence: 0.99m, title: "Anything") },
        };
        var runner = new AutoIdentifyRunner(settings, identify, db, NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("already organized", result.SkipReason);
        Assert.Empty(identify.IdentifyCalls);
    }

    [Fact]
    public async Task AppliesSeriesRootWhoseRootPatchHasNullCollections() {
        await using var db = CreateContext();
        var seriesId = await SeedVideoAsync(db, organized: false, kind: "video-series");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        // A series root often arrives with a sparse patch (null collections) and its value in Children.
        var sparsePatch = new EntityMetadataPatch(
            Title: "The Chair Company",
            Description: null,
            ExternalIds: null!,
            Urls: null!,
            Tags: null!,
            Studio: null,
            Credits: null!,
            Dates: null!,
            Stats: null!,
            Positions: null!,
            Classification: null);
        var proposal = new EntityMetadataProposal(
            ProposalId: Guid.NewGuid().ToString(),
            Provider: "p1",
            TargetKind: ProposalKind.VideoSeries,
            Confidence: null,
            MatchReason: null,
            Patch: sparsePatch,
            Images: null!,
            Children: [],
            Candidates: [],
            Relationships: null!);
        var identify = new FakeIdentifyProvider { ProposalsByProvider = { ["p1"] = proposal } };
        var runner = new AutoIdentifyRunner(settings, identify, db, NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(seriesId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Contains("title", Assert.Single(identify.ApplyCalls).Fields);
    }

    [Fact]
    public async Task SkipsChildEntitiesSoOnlyTheParentIsIdentified() {
        await using var db = CreateContext();
        var seriesId = await SeedVideoAsync(db, organized: false);
        var episodeId = await SeedVideoAsync(db, organized: false, parentId: seriesId);
        var settings = await ConfigureAsync(db, enabled: true, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = { ["p1"] = Proposal("p1", confidence: 0.99m, title: "Episode") },
        };
        var runner = new AutoIdentifyRunner(settings, identify, db, NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(episodeId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Equal("child entity; its parent is identified instead", result.SkipReason);
        Assert.Empty(identify.IdentifyCalls);
    }

    [Fact]
    public async Task MatchesProviderCapabilityByConcreteKindSoAlbumsAutoIdentify() {
        await using var db = CreateContext();
        var albumId = await SeedVideoAsync(db, organized: false, kind: EntityKindRegistry.AudioLibrary.Code, title: "Abbey Road");
        var settings = await ConfigureAsync(db, enabled: true, providers: ["musicbrainz"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider {
            ProposalsByProvider = {
                ["musicbrainz"] = Proposal("musicbrainz", confidence: 0.95m, title: "Abbey Road", targetKind: ProposalKind.AudioLibrary),
            },
            // Mirrors the MusicBrainz manifest: concrete kinds only, no generic "audio" kind, so a
            // capability lookup by the settings selector kind would wrongly exclude the provider.
            SupportedKindsByProvider = {
                ["musicbrainz"] = [
                    EntityKindRegistry.MusicArtist.Code,
                    EntityKindRegistry.AudioLibrary.Code,
                    EntityKindRegistry.AudioTrack.Code,
                ],
            },
        };
        var runner = new AutoIdentifyRunner(settings, identify, db, NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(albumId, CancellationToken.None);

        Assert.True(result.Applied);
        Assert.Equal("musicbrainz", result.Provider);
        Assert.Single(identify.ApplyCalls);
    }

    [Fact]
    public async Task SkipsWhenDisabled() {
        await using var db = CreateContext();
        var entityId = await SeedVideoAsync(db, organized: false);
        var settings = await ConfigureAsync(db, enabled: false, providers: ["p1"], confidencePercent: 90m);
        var identify = new FakeIdentifyProvider();
        var runner = new AutoIdentifyRunner(settings, identify, db, NullLogger<AutoIdentifyRunner>.Instance);

        var result = await runner.RunAsync(entityId, CancellationToken.None);

        Assert.False(result.Applied);
        Assert.Empty(identify.IdentifyCalls);
    }

    private static EntityMetadataProposal Proposal(string provider, decimal? confidence, string title, ProposalKind targetKind = ProposalKind.Video) =>
        new(
            ProposalId: Guid.NewGuid().ToString(),
            Provider: provider,
            TargetKind: targetKind,
            Confidence: confidence,
            MatchReason: null,
            Patch: new EntityMetadataPatch(
                Title: title,
                Description: "A film.",
                ExternalIds: new Dictionary<string, string> { ["tmdb"] = "603" },
                Urls: [],
                Tags: [],
                Studio: null,
                Credits: [],
                Dates: new Dictionary<string, string>(),
                Stats: new Dictionary<string, int>(),
                Positions: new Dictionary<string, int>(),
                Classification: null) {
                Rating = 4
            },
            Images: [new ImageCandidate("poster", "https://img/poster.jpg", provider, 1m, null, null, null)],
            Children: [],
            Candidates: [],
            Relationships: []);

    private static EntityMetadataProposal CandidateShell(
        string provider,
        string externalId,
        string title,
        decimal? confidence) =>
        new(
            ProposalId: null!,
            Provider: provider,
            TargetKind: ProposalKind.VideoSeries,
            Confidence: null,
            MatchReason: null,
            Patch: null!,
            Images: [],
            Children: [],
            Candidates: [
                new EntitySearchCandidate(
                    new Dictionary<string, string> { [provider] = externalId },
                    title,
                    Year: 2025,
                    Overview: null,
                    PosterUrl: null,
                    Popularity: null,
                    CandidateId: $"{provider}:tv:{externalId}",
                    Source: provider,
                    Confidence: confidence,
                    MatchReason: "title-search")
            ],
            Relationships: []);

    private static async Task<Guid> SeedVideoAsync(
        PrismediaDbContext db,
        bool organized,
        Guid? parentId = null,
        string kind = "video",
        string title = "video.mkv") {
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind,
            Title = title,
            IsOrganized = organized,
            ParentEntityId = parentId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<SettingsService> ConfigureAsync(
        PrismediaDbContext db,
        bool enabled,
        string[] providers,
        decimal confidencePercent) {
        var service = new SettingsService(new EfSettingsPersistence(db));
        await service.UpdateSettingsAsync(
            new Dictionary<string, JsonElement> {
                [AppSettingKeys.AutoIdentifyEnabled] = JsonSerializer.SerializeToElement(enabled),
                [AppSettingKeys.AutoIdentifyProviders] = JsonSerializer.SerializeToElement(providers),
                [AppSettingKeys.AutoIdentifyConfidenceThreshold] = JsonSerializer.SerializeToElement(confidencePercent),
            },
            CancellationToken.None);
        return service;
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"auto-identify-{Guid.NewGuid():N}")
            .Options;
        return new PrismediaDbContext(options);
    }

    private sealed class FakeIdentifyProvider : IIdentifyProviderService {
        public Dictionary<string, EntityMetadataProposal> ProposalsByProvider { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, EntityMetadataProposal> ProposalsByExternalId { get; } = new(StringComparer.Ordinal);
        /// <summary>Optional manifest-style declared kinds per provider; providers absent here match any kind.</summary>
        public Dictionary<string, string[]> SupportedKindsByProvider { get; } = new(StringComparer.Ordinal);
        public List<(Guid EntityId, string Provider, IdentifyQuery? Query)> IdentifyCalls { get; } = [];
        public List<(IReadOnlyCollection<string> Fields, IReadOnlyDictionary<string, string?>? SelectedImages)> ApplyCalls { get; } = [];

        public Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(string? entityKind, CancellationToken cancellationToken) {
            IReadOnlyList<PluginProvider> result = ProposalsByProvider.Keys
                .Where(id => entityKind is null ||
                    !SupportedKindsByProvider.TryGetValue(id, out var kinds) ||
                    kinds.Contains(entityKind, StringComparer.OrdinalIgnoreCase))
                .Select(id => new PluginProvider(
                    Id: id,
                    Name: id,
                    Version: "1.0.0",
                    Installed: true,
                    Enabled: true,
                    IsNsfw: false,
                    Supports: [new PluginEntitySupport(entityKind ?? "video", ["search"])],
                    Auth: [],
                    MissingAuthKeys: []))
                .ToList();
            return Task.FromResult(result);
        }

        public Task<IdentifyPluginResponse> IdentifyAsync(
            Guid entityId, string providerId, IdentifyQuery? query,
            IReadOnlyDictionary<string, string>? parentExternalIds, bool hideNsfw, CancellationToken cancellationToken,
            bool cascadeChildren = true, IIdentifyCascadeSink? sink = null) {
            IdentifyCalls.Add((entityId, providerId, query));
            if (query?.ExternalIds is not null &&
                query.ExternalIds.TryGetValue(providerId, out var externalId) &&
                ProposalsByExternalId.TryGetValue($"{providerId}:{externalId}", out var lookupProposal)) {
                return Task.FromResult(new IdentifyPluginResponse(true, lookupProposal, null));
            }

            return Task.FromResult(ProposalsByProvider.TryGetValue(providerId, out var proposal)
                ? new IdentifyPluginResponse(true, proposal, null)
                : new IdentifyPluginResponse(false, null, "no result"));
        }

        public Task<bool> ApplyAsync(
            Guid entityId,
            EntityMetadataProposal proposal,
            IReadOnlyCollection<string> selectedFields,
            IReadOnlyDictionary<string, string?>? selectedImages,
            CancellationToken cancellationToken) {
            ApplyCalls.Add((selectedFields, selectedImages));
            return Task.FromResult(true);
        }
    }
}
