using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Tests;

/// <summary>Selective imports must remove unwanted transfer data even when the profile normally seeds.</summary>
public sealed class ImportedPayloadCleanupTests {
    [Fact]
    public async Task SelectiveHardlinkImportRemovesTheTransferAndItsRemainingData() {
        await using var db = new PrismediaDbContext(
            new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var clientId = Guid.NewGuid();
        var downloadClient = new RecordingDownloadClient();
        var remover = new ImportedTorrentRemover(
            AcquisitionTestFactory.Store(db),
            new SingleDownloadClientConfigStore(new DownloadClientDetail(
                clientId,
                DownloadClientKind.QBittorrent,
                "Downloads",
                "http://download-client",
                Username: null,
                Category: "prismedia",
                Enabled: true,
                HasPassword: false,
                Password: null)),
            new SingleDownloadClientFactory(downloadClient),
            NullLogger<ImportedTorrentRemover>.Instance);
        var import = new AcquisitionImportContext(
            Guid.NewGuid(),
            "Frozen",
            Author: "Various Artists",
            Series: null,
            Year: 2013,
            PosterUrl: null,
            ExternalIdentity: null,
            ProfileId: null,
            ContentPath: "/downloads/frozen-deluxe",
            ClientItemId: "frozen-transfer",
            DownloadClientConfigId: clientId,
            Kind: EntityKind.AudioLibrary);

        await remover.HandleImportedAsync(
            import,
            ImportMode.Hardlink,
            discardRemainingPayload: true,
            CancellationToken.None);

        Assert.Equal("frozen-transfer", downloadClient.RemovedClientItemId);
        Assert.True(downloadClient.DeletedData);
    }

    private sealed class SingleDownloadClientFactory(IDownloadClient client) : IDownloadClientFactory {
        public IDownloadClient Get(DownloadClientKind kind) => client;
    }

    private sealed class RecordingDownloadClient : IDownloadClient {
        public DownloadClientKind Kind => DownloadClientKind.QBittorrent;
        public string? RemovedClientItemId { get; private set; }
        public bool DeletedData { get; private set; }

        public Task RemoveAsync(
            DownloadClientConnection connection,
            string clientItemId,
            bool deleteData,
            CancellationToken cancellationToken) {
            RemovedClientItemId = clientItemId;
            DeletedData = deleteData;
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

    private sealed class SingleDownloadClientConfigStore(DownloadClientDetail detail) : IDownloadClientConfigStore {
        public Task<DownloadClientDetail?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<DownloadClientDetail?>(id == detail.Id ? detail : null);

        public Task<DownloadClientDetail?> GetDefaultAsync(CancellationToken cancellationToken) =>
            Task.FromResult<DownloadClientDetail?>(detail);

        public Task<IReadOnlyList<DownloadClientSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientDetail>> ListDetailsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientDetail?> GetDefaultAsync(DownloadProtocol protocol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadClientDetail>> ListEnabledAsync(DownloadProtocol protocol, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadProtocol>> GetEnabledProtocolsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DownloadClientSummary> SaveAsync(DownloadClientSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
