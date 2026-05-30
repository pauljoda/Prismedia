using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Plugins;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Plugins;
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

    private static EntityMetadataProposal Proposal(string provider, decimal? confidence, string title) =>
        new(
            ProposalId: Guid.NewGuid().ToString(),
            Provider: provider,
            TargetKind: "video",
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
                Classification: null),
            Images: [new ImageCandidate("poster", "https://img/poster.jpg", provider, 1m, null, null, null)],
            Children: [],
            Candidates: [],
            Relationships: []);

    private static async Task<Guid> SeedVideoAsync(PrismediaDbContext db, bool organized) {
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = "video",
            Title = "video.mkv",
            IsOrganized = organized,
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
        public List<(Guid EntityId, string Provider)> IdentifyCalls { get; } = [];
        public List<(IReadOnlyCollection<string> Fields, IReadOnlyDictionary<string, string?>? SelectedImages)> ApplyCalls { get; } = [];

        public Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(string? entityKind, CancellationToken cancellationToken) {
            IReadOnlyList<PluginProvider> result = ProposalsByProvider.Keys
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
            Guid entityId, string providerId, IdentifyQuery? query, bool hideNsfw, CancellationToken cancellationToken) {
            IdentifyCalls.Add((entityId, providerId));
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
