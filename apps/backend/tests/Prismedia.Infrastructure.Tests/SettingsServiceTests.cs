using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed class SettingsServiceTests {
    [Fact]
    public async Task CreateLibraryRootQueuesScanForEachEnabledKind() {
        await using var db = CreateContext();
        var queue = new RecordingJobQueue();
        var service = new SettingsService(new EfSettingsPersistence(db), queue);

        await service.CreateLibraryRootAsync(
            new LibraryRootCreateRequest(
                Path: "/media/comics",
                Label: "Comics",
                Enabled: true,
                Recursive: true,
                ScanVideos: false,
                ScanImages: false,
                ScanAudio: false,
                ScanBooks: true,
                IsNsfw: false),
            CancellationToken.None);

        // A books-only library must queue a book scan and nothing else.
        Assert.Equal([JobType.ScanBook], queue.Enqueued.Select(request => request.Type));
    }

    [Theory]
    [MemberData(nameof(CreateLibraryRootScanKindCases))]
    public async Task CreateLibraryRootQueuesOnlySelectedScanKinds(
        bool scanVideos,
        bool scanImages,
        bool scanAudio,
        bool scanBooks,
        JobType[] expectedTypes) {
        await using var db = CreateContext();
        var queue = new RecordingJobQueue();
        var service = new SettingsService(new EfSettingsPersistence(db), queue);

        await service.CreateLibraryRootAsync(
            new LibraryRootCreateRequest(
                Path: "/media/selected",
                Label: "Selected",
                Enabled: true,
                Recursive: true,
                ScanVideos: scanVideos,
                ScanImages: scanImages,
                ScanAudio: scanAudio,
                ScanBooks: scanBooks,
                IsNsfw: false),
            CancellationToken.None);

        Assert.Equal(expectedTypes, queue.Enqueued.Select(request => request.Type));
    }

    [Fact]
    public async Task CreateLibraryRootQueuesAllDefaultKinds() {
        await using var db = CreateContext();
        var queue = new RecordingJobQueue();
        var service = new SettingsService(new EfSettingsPersistence(db), queue);

        // Omitting the scan flags uses the defaults (videos, images, audio on; books off).
        await service.CreateLibraryRootAsync(
            new LibraryRootCreateRequest(
                Path: "/media/library",
                Label: null,
                Enabled: null,
                Recursive: null,
                ScanVideos: null,
                ScanImages: null,
                ScanAudio: null,
                ScanBooks: null,
                IsNsfw: null),
            CancellationToken.None);

        Assert.Equal(
            [JobType.ScanLibrary, JobType.ScanGallery, JobType.ScanAudio],
            queue.Enqueued.Select(request => request.Type));
    }

    [Fact]
    public async Task CreateDisabledLibraryRootQueuesNoScans() {
        await using var db = CreateContext();
        var queue = new RecordingJobQueue();
        var service = new SettingsService(new EfSettingsPersistence(db), queue);

        await service.CreateLibraryRootAsync(
            new LibraryRootCreateRequest(
                Path: "/media/library",
                Label: null,
                Enabled: false,
                Recursive: null,
                ScanVideos: null,
                ScanImages: null,
                ScanAudio: null,
                ScanBooks: null,
                IsNsfw: null),
            CancellationToken.None);

        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task CatalogUsesRegistryDefaultsWithoutCreatingRows() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        var catalog = await service.GetCatalogAsync(CancellationToken.None);
        var castControls = catalog.Groups
            .SelectMany(group => group.Settings)
            .Single(setting => setting.Key == AppSettingKeys.PlaybackShowCastControls);
        var hlsTranscoder = catalog.Groups
            .SelectMany(group => group.Settings)
            .Single(setting => setting.Key == AppSettingKeys.HlsTranscoderProfile);

        Assert.True(castControls.Value.GetBoolean());
        Assert.True(castControls.IsDefault);
        Assert.Equal("Auto", hlsTranscoder.Value.GetString());
        Assert.True(hlsTranscoder.IsDefault);
        Assert.Empty(await db.AppSettings.ToArrayAsync());
    }

    [Fact]
    public async Task UpdatePersistsOnlyNonDefaultOverrides() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        await service.UpdateSettingAsync(
            AppSettingKeys.HlsTranscoderProfile,
            JsonSerializer.SerializeToElement("VideoToolbox"),
            CancellationToken.None);

        var row = await db.AppSettings.SingleAsync();
        Assert.Equal(AppSettingKeys.HlsTranscoderProfile, row.Key);
        Assert.Equal("\"VideoToolbox\"", row.ValueJson);
    }

    [Fact]
    public async Task BatchUpdatePersistsStringListAndPaths() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        await service.UpdateSettingsAsync(
            new Dictionary<string, JsonElement> {
                [AppSettingKeys.PlaybackAudioPreferredLanguages] =
                    JsonSerializer.SerializeToElement(new[] { "ja", "jpn" }),
                [AppSettingKeys.HlsFfmpegPath] =
                    JsonSerializer.SerializeToElement("/opt/homebrew/bin/ffmpeg"),
            },
            CancellationToken.None);

        var playback = await service.GetPlaybackSettingsAsync(CancellationToken.None);
        var hls = await service.GetHlsSettingsAsync(CancellationToken.None);

        Assert.Equal(["ja", "jpn"], playback.AudioPreferredLanguages);
        Assert.Equal("/opt/homebrew/bin/ffmpeg", hls.FfmpegPath);
        Assert.Equal(2, await db.AppSettings.CountAsync());
    }

    [Fact]
    public async Task AutoIdentifySettingsDefaultOffWithFullKindCoverage() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        var settings = await service.GetAutoIdentifySettingsAsync(CancellationToken.None);

        Assert.False(settings.Enabled);
        Assert.Empty(settings.Providers);
        Assert.Equal(["video", "gallery", "image", "audio", "book"], settings.EntityKinds);
        // Stored as a 0–100 percentage (default 90), surfaced to backend consumers as a 0–1 fraction.
        Assert.Equal(0.9d, settings.ConfidenceThreshold, 3);
        Assert.True(settings.UnorganizedOnly);
        Assert.Empty(await db.AppSettings.ToArrayAsync());
    }

    [Fact]
    public async Task AutoIdentifySettingsConvertThresholdAndPreserveProviderOrder() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        await service.UpdateSettingsAsync(
            new Dictionary<string, JsonElement> {
                [AppSettingKeys.AutoIdentifyEnabled] = JsonSerializer.SerializeToElement(true),
                [AppSettingKeys.AutoIdentifyConfidenceThreshold] = JsonSerializer.SerializeToElement(75m),
                [AppSettingKeys.AutoIdentifyProviders] =
                    JsonSerializer.SerializeToElement(new[] { "tmdb", "stash-erome" }),
                [AppSettingKeys.AutoIdentifyEntityKinds] =
                    JsonSerializer.SerializeToElement(new[] { "video" }),
                [AppSettingKeys.AutoIdentifyUnorganizedOnly] = JsonSerializer.SerializeToElement(false),
            },
            CancellationToken.None);

        var settings = await service.GetAutoIdentifySettingsAsync(CancellationToken.None);

        Assert.True(settings.Enabled);
        Assert.Equal(0.75d, settings.ConfidenceThreshold, 3);
        Assert.Equal(["tmdb", "stash-erome"], settings.Providers);
        Assert.Equal(["video"], settings.EntityKinds);
        Assert.False(settings.UnorganizedOnly);
    }

    [Fact]
    public async Task SavingDecimalDefaultRemovesOverride() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        await service.UpdateSettingAsync(
            AppSettingKeys.SubtitlesOpacity,
            JsonSerializer.SerializeToElement(0.8m),
            CancellationToken.None);
        var reset = await service.UpdateSettingAsync(
            AppSettingKeys.SubtitlesOpacity,
            JsonSerializer.SerializeToElement(1.0m),
            CancellationToken.None);

        Assert.True(reset.IsDefault);
        Assert.Empty(await db.AppSettings.ToArrayAsync());
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"settings-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }

    public static TheoryData<bool, bool, bool, bool, JobType[]> CreateLibraryRootScanKindCases() =>
        new() {
            { false, true, false, false, [JobType.ScanGallery] },
            { false, false, true, false, [JobType.ScanAudio] },
            { false, true, true, true, [JobType.ScanGallery, JobType.ScanAudio, JobType.ScanBook] },
        };

    /// <summary>
    /// Minimal <see cref="IJobQueueService"/> test double that records enqueued requests and
    /// reports no pending work so scan deduplication never suppresses an enqueue.
    /// </summary>
    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Enqueued { get; } = [];

        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Enqueued.Add(request);
            return Task.FromResult(new JobRunSnapshot(
                Guid.NewGuid(),
                request.Type,
                JobRunStatus.Queued,
                0,
                null,
                request.PayloadJson ?? "{}",
                request.TargetEntityKind,
                request.TargetEntityId,
                request.TargetLabel,
                DateTimeOffset.UtcNow,
                null,
                null));
        }

        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) =>
            EnqueueAsync(new EnqueueJobRequest(type), cancellationToken);

        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobRunSnapshot>>([]);

        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) {
            Enqueued.AddRange(requests);
            return Task.FromResult(requests.Count);
        }

        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken) =>
            Task.FromResult<JobRunSnapshot?>(null);

        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobQueueCount>>([]);

        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
