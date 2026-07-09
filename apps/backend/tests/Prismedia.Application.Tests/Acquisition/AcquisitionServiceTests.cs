using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionServiceTests {
    private static readonly Guid AcquisitionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid WantedEntityId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid DefaultClientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RecordedClientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string ClientItemId = "download-owned-by-recorded-client";

    [Fact]
    public async Task CreateNormalizesTheNamespaceAndPreservesAnOpaqueColonIdentityValue() {
        var harness = Harness(TransferInfo(RecordedClientId));

        await harness.Service.CreateAndSearchAsync(
            new AcquisitionCreateRequest(
                "Show", null, null, null, null,
                " TMDB ", " Series:Episode:AbC ",
                Kind: EntityKind.VideoSeries),
            CancellationToken.None);

        Assert.Equal(
            new ExternalIdentity("tmdb", "Series:Episode:AbC"),
            harness.Store.CreatedMetadata!.ExternalIdentity);
        Assert.Equal(
            [JobType.AcquisitionSearch, JobType.AcquisitionEnrich],
            harness.Queue.Requests.Select(request => request.Type).ToArray());
    }

    [Fact]
    public async Task CreateRejectsAPartialExternalIdentity() {
        var harness = Harness(TransferInfo(RecordedClientId));

        var exception = await Assert.ThrowsAsync<AcquisitionConfigurationException>(() =>
            harness.Service.CreateAndSearchAsync(
                new AcquisitionCreateRequest(
                    "Show", null, null, null, null,
                    IdentityNamespace: "tmdb", IdentityValue: null),
                CancellationToken.None));

        Assert.Equal(ApiProblemCodes.AcquisitionInvalid, exception.Code);
        Assert.Null(harness.Store.CreatedMetadata);
        Assert.Empty(harness.Queue.Requests);
    }

    [Fact]
    public async Task CancelAsyncRemovesTransferFromTheRecordedClient() {
        var harness = Harness(TransferInfo(RecordedClientId));

        await harness.Service.CancelAsync(AcquisitionId, CancellationToken.None);

        Assert.Equal([(RecordedClientId, ClientItemId, true)], harness.Downloads.Removals);
        Assert.Equal(AcquisitionStatus.Cancelled, harness.Store.Status);
        // Cancel stops the download only — the wanted placeholder and its monitor stay untouched.
        Assert.Empty(harness.Monitors.Retargets);
        var entry = Assert.Single(harness.History.Entries);
        Assert.Equal(AcquisitionHistoryEvent.Removed, entry.Event);
        Assert.Equal("Cancelled by user.", entry.Message);
    }

    [Fact]
    public async Task DeleteAsyncRemovesTransferFromTheRecordedClient() {
        var harness = Harness(TransferInfo(RecordedClientId));

        Assert.True(await harness.Service.DeleteAsync(AcquisitionId, CancellationToken.None));

        Assert.Equal([(RecordedClientId, ClientItemId, true)], harness.Downloads.Removals);
        Assert.True(harness.Store.Deleted);
        var entry = Assert.Single(harness.History.Entries);
        Assert.Equal(AcquisitionHistoryEvent.Removed, entry.Event);
        Assert.Equal("Removed by user.", entry.Message);
    }

    [Fact]
    public async Task CancelAsyncDoesNotRemoveFromDefaultWhenRecordedClientIsMissing() {
        var harness = Harness(TransferInfo(RecordedClientId), includeRecordedClient: false);

        await harness.Service.CancelAsync(AcquisitionId, CancellationToken.None);

        Assert.Empty(harness.Downloads.Removals);
        Assert.Equal(AcquisitionStatus.Cancelled, harness.Store.Status);
        Assert.Empty(harness.Monitors.Retargets);
        var entry = Assert.Single(harness.History.Entries);
        Assert.Equal(AcquisitionHistoryEvent.Removed, entry.Event);
        Assert.Equal("Cancelled by user.", entry.Message);
    }

    [Fact]
    public async Task DeleteAsyncDoesNotRemoveFromDefaultWhenRecordedClientIsMissing() {
        var harness = Harness(TransferInfo(RecordedClientId), includeRecordedClient: false);

        Assert.True(await harness.Service.DeleteAsync(AcquisitionId, CancellationToken.None));

        Assert.Empty(harness.Downloads.Removals);
        Assert.True(harness.Store.Deleted);
        var entry = Assert.Single(harness.History.Entries);
        Assert.Equal(AcquisitionHistoryEvent.Removed, entry.Event);
        Assert.Equal("Removed by user.", entry.Message);
    }

    [Fact]
    public async Task DeleteAsyncWithPreserveWantedLoopRetargetsTheMonitorAtTheClone() {
        var harness = Harness(TransferInfo(RecordedClientId));
        var cloneId = Guid.NewGuid();
        harness.Store.CloneResult = cloneId;

        Assert.True(await harness.Service.DeleteAsync(AcquisitionId, CancellationToken.None, preserveWantedLoop: true));

        // The download and record are gone, but the monitoring loop survives on the fresh clone.
        Assert.True(harness.Store.Deleted);
        Assert.Equal([(AcquisitionId, cloneId)], harness.Monitors.Retargets);
    }

    [Fact]
    public async Task DeleteAsyncWithoutPreserveNeverClonesOrRetargets() {
        var harness = Harness(TransferInfo(RecordedClientId));
        harness.Store.CloneResult = Guid.NewGuid();

        Assert.True(await harness.Service.DeleteAsync(AcquisitionId, CancellationToken.None));

        Assert.True(harness.Store.Deleted);
        Assert.Empty(harness.Monitors.Retargets);
    }

    [Fact]
    public async Task DeleteAsyncUsesDefaultClientOnlyForLegacyTransfersWithoutRecordedClient() {
        var harness = Harness(TransferInfo(downloadClientConfigId: null));

        Assert.True(await harness.Service.DeleteAsync(AcquisitionId, CancellationToken.None));

        Assert.Equal([(DefaultClientId, ClientItemId, true)], harness.Downloads.Removals);
        Assert.True(harness.Store.Deleted);
    }

    private static AcquisitionTransferInfo TransferInfo(Guid? downloadClientConfigId) =>
        new(AcquisitionStatus.Downloading, FinalSourcePath: null, ClientItemId, downloadClientConfigId);

    private static TestHarness Harness(AcquisitionTransferInfo transfer, bool includeRecordedClient = true) {
        var store = new FakeAcquisitionStore(transfer);
        var downloads = new RecordingDownloadClientFactory();
        var configs = new FakeDownloadClientConfigStore(includeRecordedClient);
        var history = new FakeAcquisitionHistoryStore();
        var monitors = new RecordingMonitorStore();
        var queue = new RecordingJobQueue();
        var service = new AcquisitionService(
            store,
            new ThrowingBlocklistStore(),
            queue,
            configs,
            downloads,
            new EmptyImportedFilesReader(),
            history,
            NullLogger<AcquisitionService>.Instance,
            monitors);

        return new TestHarness(service, store, downloads, history, monitors, queue);
    }

    private sealed record TestHarness(
        AcquisitionService Service,
        FakeAcquisitionStore Store,
        RecordingDownloadClientFactory Downloads,
        FakeAcquisitionHistoryStore History,
        RecordingMonitorStore Monitors,
        RecordingJobQueue Queue);

    private sealed class FakeAcquisitionStore(AcquisitionTransferInfo transfer) : IAcquisitionStore {
        private readonly AcquisitionSummary _summary = new(
            AcquisitionId,
            AcquisitionStatus.Downloading,
            null,
            "Dune",
            "Frank Herbert",
            null,
            1965,
            null,
            0.25,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            Kind: EntityKind.Book,
            EntityId: WantedEntityId);

        public AcquisitionStatus? Status { get; private set; } = AcquisitionStatus.Downloading;
        public bool Deleted { get; private set; }
        public AcquisitionMetadata? CreatedMetadata { get; private set; }

        public Task<AcquisitionDetail?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<AcquisitionDetail?>(id == AcquisitionId
                ? new AcquisitionDetail(_summary with { Status = Status ?? _summary.Status }, [])
                : null);

        public Task SetStatusAsync(Guid id, AcquisitionStatus status, string? message, CancellationToken cancellationToken) {
            Assert.Equal(AcquisitionId, id);
            Status = status;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) {
            Assert.Equal(AcquisitionId, id);
            Deleted = true;
            return Task.FromResult(true);
        }

        /// <summary>What CloneForRetryAsync returns — set by tests exercising the preserve-wanted-loop path.</summary>
        public Guid? CloneResult { get; set; }

        public Task<Guid?> CloneForRetryAsync(Guid id, CancellationToken cancellationToken) {
            Assert.Equal(AcquisitionId, id);
            return Task.FromResult(CloneResult);
        }

        public Task<AcquisitionTransferInfo?> GetTransferInfoAsync(Guid acquisitionId, CancellationToken cancellationToken) =>
            Task.FromResult<AcquisitionTransferInfo?>(acquisitionId == AcquisitionId ? transfer : null);

        public Task<AcquisitionSummary> CreateAsync(AcquisitionMetadata metadata, CancellationToken cancellationToken) {
            CreatedMetadata = metadata;
            return Task.FromResult(_summary with {
                Status = AcquisitionStatus.Pending,
                Title = metadata.Title,
                Kind = metadata.Kind,
                EntityId = metadata.EntityId
            });
        }
        public Task<IReadOnlyList<Guid>> ListStaleSearchingAsync(TimeSpan olderThan, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AcquisitionSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AcquisitionSearchInput?> GetSearchInputAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AcquisitionStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UpgradeOwnedQuality?> GetUpgradeOwnedQualityAsync(Guid acquisitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UpgradeReplaceTarget?> GetUpgradeReplaceTargetAsync(Guid childId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateOwnedQualityAsync(Guid acquisitionId, BookQualityRank ownedQuality, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateOwnedMediaQualityAsync(Guid acquisitionId, string ownedMediaQuality, int ownedMediaRevision, int ownedFormatScore, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnrichMetadataAsync(Guid acquisitionId, string? description, string? posterUrl, int? year, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkImportedWithQualityAsync(Guid id, BookQualityRank ownedQuality, string? message, CancellationToken cancellationToken, string? ownedMediaQuality = null, int ownedMediaRevision = 1, int ownedFormatScore = 0) => throw new NotSupportedException();
        public Task ReplaceCandidatesAsync(Guid id, IReadOnlyList<ScoredRelease> candidates, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AcquisitionQueueCandidate?> GetQueueCandidateAsync(Guid acquisitionId, Guid candidateId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AcquisitionCandidateRef>> ListAcceptedCandidatesAsync(Guid acquisitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkCandidatesBlocklistedAsync(Guid acquisitionId, string identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetSelectedReleaseAsync(Guid acquisitionId, SelectedRelease selected, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SelectedRelease?> GetSelectedReleaseAsync(Guid acquisitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateTransferAsync(Guid acquisitionId, Guid? downloadClientConfigId, string clientItemId, string? category, CancellationToken cancellationToken, TransferSeedGoal? seedGoal = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<SeedingTransfer>> ListSeedingTransfersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkTransferSeedingAsync(Guid acquisitionId, DateTimeOffset since, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ClearTransferSeedingAsync(Guid transferId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActiveTransfer>> ListActiveTransfersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasActiveTransfersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateTransferAsync(Guid transferId, double progress, string? state, string? contentPath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkTransferStalledAsync(Guid transferId, DateTimeOffset? stalledSince, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AcquisitionImportContext?> GetImportContextAsync(Guid acquisitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetFinalSourcePathAsync(Guid acquisitionId, string finalSourcePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task WriteImportHintAsync(Guid acquisitionId, string sourcePath, AcquisitionImportContext context, BookQualityRank ownedQuality, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> AnyForEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AcquisitionDetail?> GetLatestForEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ListIdsForEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeDownloadClientConfigStore(bool includeRecordedClient) : IDownloadClientConfigStore {
        private static readonly DownloadClientDetail Default = new(
            DefaultClientId, DownloadClientKind.QBittorrent, "Default qBittorrent", "http://qbit", "admin", "prismedia", true, true, "secret");
        private static readonly DownloadClientDetail Recorded = new(
            RecordedClientId, DownloadClientKind.Transmission, "Recorded Transmission", "http://transmission", "user", "prismedia", true, true, "secret");

        public Task<DownloadClientDetail?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<DownloadClientDetail?>(includeRecordedClient && id == RecordedClientId ? Recorded : null);

        public Task<DownloadClientDetail?> GetDefaultAsync(CancellationToken cancellationToken) =>
            Task.FromResult<DownloadClientDetail?>(Default);

        public Task<IReadOnlyList<DownloadClientSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientDetail>> ListDetailsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientDetail?> GetDefaultAsync(DownloadProtocol protocol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientDetail>> ListEnabledAsync(DownloadProtocol protocol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadProtocol>> GetEnabledProtocolsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientSummary> SaveAsync(DownloadClientSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingDownloadClientFactory : IDownloadClientFactory {
        private readonly RecordingDownloadClient _client = new();
        public List<(Guid ClientId, string ClientItemId, bool DeleteData)> Removals => _client.Removals;
        public IDownloadClient Get(DownloadClientKind kind) => _client;
    }

    private sealed class RecordingDownloadClient : IDownloadClient {
        public DownloadClientKind Kind => DownloadClientKind.QBittorrent;
        public List<(Guid ClientId, string ClientItemId, bool DeleteData)> Removals { get; } = [];

        public Task RemoveAsync(DownloadClientConnection connection, string clientItemId, bool deleteData, CancellationToken cancellationToken) {
            Removals.Add((connection.Id, clientItemId, deleteData));
            return Task.CompletedTask;
        }

        public Task<string> AddAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> AddTorrentFileAsync(DownloadClientConnection connection, string fileName, byte[] torrent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadItemStatus?> GetItemAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadItemStatus>> ListItemsAsync(DownloadClientConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadItemFile>> GetFilesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadItemProperties?> GetPropertiesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> GetPieceStatesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientConnectionTest> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAcquisitionHistoryStore : IAcquisitionHistoryStore {
        public List<AcquisitionHistoryEntry> Entries { get; } = [];
        public Task AddAsync(AcquisitionHistoryEntry entry, CancellationToken cancellationToken) {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AcquisitionHistoryView>> ListAsync(int limit, Guid? entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Minimal monitor-store fake recording retargets — the only member the service's delete path uses.</summary>
    private sealed class RecordingMonitorStore : IMonitorStore {
        public List<(Guid From, Guid To)> Retargets { get; } = [];

        public Task<bool> RetargetAsync(Guid fromAcquisitionId, Guid toAcquisitionId, CancellationToken cancellationToken) {
            Retargets.Add((fromAcquisitionId, toAcquisitionId));
            return Task.FromResult(true);
        }

        public Task<MonitorView> StartAsync(Guid acquisitionId, EntityKind kind, string title, string? author, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView> StartForEntityAsync(Guid entityId, EntityKind kind, string title, AcquisitionTargeting? targeting, MonitorPreset? preset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView?> GetByEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AcquisitionTargeting?> GetTargetingByEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorPreset?> GetPresetByEntityAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid monitorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SetStatusAsync(Guid monitorId, MonitorStatus status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MonitorView>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WantedPage> ListMissingAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WantedPage> ListCutoffUnmetAsync(int page, int pageSize, EntityKind? kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorView?> GetByAcquisitionAsync(Guid acquisitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasActiveMonitorsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DueMonitor>> ListDueMonitorsAsync(int defaultIntervalMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkSearchedAsync(Guid monitorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> CreateUpgradeChildAsync(Guid monitorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResolveUpgradeChildAsync(Guid childId, bool succeeded, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptyImportedFilesReader : IImportedFilesReader {
        public IReadOnlyList<DownloadItemFile> List(string path) => [];
    }

    private sealed class ThrowingBlocklistStore : IAcquisitionBlocklistStore {
        public Task<IReadOnlySet<string>> GetIdentitiesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AcquisitionBlocklistEntry>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(BlocklistAddRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingJobQueue : IJobQueueService {
        public List<EnqueueJobRequest> Requests { get; } = [];

        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) {
            Requests.Add(request);
            return Task.FromResult(new JobRunSnapshot(
                Guid.NewGuid(), request.Type, JobRunStatus.Queued, 0, null,
                request.PayloadJson ?? "{}", null, request.TargetEntityId, request.TargetLabel,
                DateTimeOffset.UtcNow, null, null));
        }
        public Task<bool> HasPendingAsync(JobType type, string? targetEntityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> EnqueueBatchAsync(IReadOnlyList<EnqueueJobRequest> requests, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CancelAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ClearFailuresAsync(JobType? type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot?> ClaimNextAsync(string workerId, CancellationToken cancellationToken, JobRunLane? lane = null) => throw new NotSupportedException();
        public Task<int> RecoverStaleRunningAsync(string currentWorkerId, TimeSpan staleAfter, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressAsync(Guid id, int progress, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string? message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid id, string message, TimeSpan retryDelay, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobQueueCount>> GetQueueCountsAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PruneHistoryAsync(TimeSpan retention, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
