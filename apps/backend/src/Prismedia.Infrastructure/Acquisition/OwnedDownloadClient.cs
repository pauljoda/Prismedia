using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Preserves adapter behavior while routing acquisition-owned deletion through the shared ownership boundary.</summary>
internal sealed class OwnedDownloadClient(IDownloadClient inner, IAcquisitionDownloadRemoval removal) : IDownloadClient {
    public DownloadClientKind Kind => inner.Kind;
    public bool DeletesCompletedPayload => inner.DeletesCompletedPayload;
    public Task<IReadOnlyList<string>> GetCompletedDirectoriesAsync(DownloadClientConnection connection, CancellationToken token) => inner.GetCompletedDirectoriesAsync(connection, token);
    public Task<string> AddAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken token) => inner.AddAsync(connection, request, token);
    public Task<string> AddTorrentFileAsync(DownloadClientConnection connection, string fileName, byte[] payload, CancellationToken token) => inner.AddTorrentFileAsync(connection, fileName, payload, token);
    public Task<DownloadItemStatus?> GetItemAsync(DownloadClientConnection connection, string itemId, CancellationToken token) => inner.GetItemAsync(connection, itemId, token);
    public Task<IReadOnlyList<DownloadItemStatus>> ListItemsAsync(DownloadClientConnection connection, CancellationToken token) => inner.ListItemsAsync(connection, token);
    public Task<IReadOnlyList<DownloadItemFile>> GetFilesAsync(DownloadClientConnection connection, string itemId, CancellationToken token) => inner.GetFilesAsync(connection, itemId, token);
    public Task<DownloadItemProperties?> GetPropertiesAsync(DownloadClientConnection connection, string itemId, CancellationToken token) => inner.GetPropertiesAsync(connection, itemId, token);
    public Task<byte[]> GetPieceStatesAsync(DownloadClientConnection connection, string itemId, CancellationToken token) => inner.GetPieceStatesAsync(connection, itemId, token);
    public Task<DownloadClientConnectionTest> TestAsync(DownloadClientConnection connection, CancellationToken token) => inner.TestAsync(connection, token);
    public Task RemoveAsync(DownloadClientConnection connection, string itemId, bool deleteData, CancellationToken token) => inner.RemoveAsync(connection, itemId, deleteData, token);
    public Task RemoveOwnedAsync(DownloadClientConnection connection, Guid? acquisitionId, string itemId,
        bool deleteData, CancellationToken token, Guid? transferId = null) =>
        removal.RemoveAsync(inner, connection, acquisitionId, itemId, deleteData, token, transferId);
}
