namespace Prismedia.Application.Acquisition;

/// <summary>Authorizes starting payload bytes only while an exact acquisition still owns its active transfer.</summary>
public interface IAcquisitionDownloadAdmission {
    /// <summary>Serializes admission with cancellation, removal and replacement of the current transfer.</summary>
    Task ReleaseAsync(IDownloadClient client, DownloadClientConnection connection, Guid acquisitionId,
        Guid transferId, string clientItemId, CancellationToken cancellationToken);
}
