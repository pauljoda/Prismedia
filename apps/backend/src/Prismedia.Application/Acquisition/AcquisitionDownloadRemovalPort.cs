namespace Prismedia.Application.Acquisition;

/// <summary>Serializes destructive client-item removal against new downloads and other owners of the same payload.</summary>
public interface IAcquisitionDownloadRemoval {
    /// <summary>Revalidates the exact owner and shared seeding goals before deleting an owned payload and its remote item.</summary>
    Task RemoveAsync(IDownloadClient client, DownloadClientConnection connection, Guid? acquisitionId,
        string clientItemId, bool deleteData, CancellationToken cancellationToken, Guid? transferId = null);
}
