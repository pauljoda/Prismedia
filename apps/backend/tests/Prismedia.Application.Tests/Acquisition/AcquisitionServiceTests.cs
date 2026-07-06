using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class AcquisitionServiceTests {
    private static readonly Guid AcquisitionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid WantedEntityId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid DefaultClientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RecordedClientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string ClientItemId = "download-owned-by-recorded-client";

    [Fact]
    public async Task CancelAsyncRemovesTransferFromTheRecordedClient() {
        var harness = Harness(TransferInfo(RecordedClientId));

        await harness.Service.CancelAsync(AcquisitionId, CancellationToken.None);

        Assert.Equal([(RecordedClientId, ClientItemId, true)], harness.Downloads.Removals);
        Assert.Equal(AcquisitionStatus.Cancelled, harness.Store.Status);
        Assert.Equal([WantedEntityId], harness.Wanted.DeletedEntities);
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
        Assert.Equal([WantedEntityId], harness.Wanted.DeletedEntities);
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
        Assert.Equal([WantedEntityId], harness.Wanted.DeletedEntities);
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
        Assert.Equal([WantedEntityId], harness.Wanted.DeletedEntities);
        var entry = Assert.Single(harness.History.Entries);
        Assert.Equal(AcquisitionHistoryEvent.Removed, entry.Event);
        Assert.Equal("Removed by user.", entry.Message);
    }

    [Fact]
    public async Task DeleteAsyncUsesDefaultClientOnlyForLegacyTransfersWithoutRecordedClient() {
        var harness = Harness(TransferInfo(downloadClientConfigId: null));

        Assert.True(await harness.Service.DeleteAsync(AcquisitionId, CancellationToken.None));

        Assert.Equal([(DefaultClientId, ClientItemId, true)], harness.Downloads.Removals);
        Assert.True(harness.Store.Deleted);
        Assert.Equal([WantedEntityId], harness.Wanted.DeletedEntities);
    }

    private static AcquisitionTransferInfo TransferInfo(Guid? downloadClientConfigId) =>
        new(AcquisitionStatus.Downloading, FinalSourcePath: null, ClientItemId, downloadClientConfigId);

    private static TestHarness Harness(AcquisitionTransferInfo transfer, bool includeRecordedClient = true) {
        var store = new FakeAcquisitionStore(transfer);
        var downloads = new RecordingDownloadClientFactory();
        var configs = new FakeDownloadClientConfigStore(includeRecordedClient);
        var history = new FakeAcquisitionHistoryStore();
        var wanted = new FakeWantedEntityWriter();
        var service = new AcquisitionService(
            store,
            new ThrowingBlocklistStore(),
            new ThrowingJobQueue(),
            configs,
            downloads,
            new EmptyImportedFilesReader(),
            history,
            NullLogger<AcquisitionService>.Instance,
            wanted);

        return new TestHarness(service, store, downloads, history, wanted);
    }

    private sealed record TestHarness(
        AcquisitionService Service,
        FakeAcquisitionStore Store,
        RecordingDownloadClientFactory Downloads,
        FakeAcquisitionHistoryStore History,
        FakeWantedEntityWriter Wanted);

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

        public Task<AcquisitionTransferInfo?> GetTransferInfoAsync(Guid acquisitionId, CancellationToken cancellationToken) =>
            Task.FromResult<AcquisitionTransferInfo?>(acquisitionId == AcquisitionId ? transfer : null);

        public Task<AcquisitionSummary> CreateAsync(AcquisitionMetadata metadata, CancellationToken cancellationToken) => throw new NotSupportedException();
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

    private sealed class FakeWantedEntityWriter : IWantedEntityWriter {
        public List<Guid> DeletedEntities { get; } = [];

        public Task<bool> DeleteIfWantedAsync(Guid entityId, CancellationToken cancellationToken) {
            DeletedEntities.Add(entityId);
            return Task.FromResult(true);
        }

        public Task<WantedEntityResult> EnsureAsync(EntityKind kind, string providerId, string itemId, string title, Guid? parentEntityId, bool matchTitleKindWide, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ApplyProposalAsync(Guid entityId, EntityMetadataProposal proposal, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MonitorableContainer?> GetContainerAsync(Guid entityId, CancellationToken cancellationToken) => throw new NotSupportedException();
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

    private sealed class ThrowingJobQueue : IJobQueueService {
        public Task<IReadOnlyList<JobRunSnapshot>> ListAsync(bool hideNsfw, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(JobType type, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobRunSnapshot> EnqueueAsync(EnqueueJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
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
