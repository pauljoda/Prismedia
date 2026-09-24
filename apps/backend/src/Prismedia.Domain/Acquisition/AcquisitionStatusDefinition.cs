using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Acquisition;

/// <summary>
/// The behavior of one <see cref="AcquisitionStatus"/> that other fulfillment owners depend on. A native
/// acquisition keeps its work and scope until it is imported or cancelled: a failed acquisition can still be
/// retried, so it continues to own that scope and a connected manager cannot take it over.
/// </summary>
public sealed class AcquisitionStatusDefinition {
    #region Static Variables

    /// <summary>Created from a request but no release search has run yet.</summary>
    public static readonly AcquisitionStatusDefinition Pending = new(AcquisitionStatus.Pending, ownsFulfillment: true);

    /// <summary>The profile is holding automatic searches until its selected release milestone.</summary>
    public static readonly AcquisitionStatusDefinition WaitingForRelease = new(AcquisitionStatus.WaitingForRelease, ownsFulfillment: true);

    /// <summary>Legacy release-gate hold retained for wire and database compatibility.</summary>
    public static readonly AcquisitionStatusDefinition ManualSearchRequired = new(AcquisitionStatus.ManualSearchRequired, ownsFulfillment: true);

    /// <summary>An indexer search is running.</summary>
    public static readonly AcquisitionStatusDefinition Searching = new(AcquisitionStatus.Searching, ownsFulfillment: true);

    /// <summary>Releases were found and a person must choose one.</summary>
    public static readonly AcquisitionStatusDefinition AwaitingSelection = new(AcquisitionStatus.AwaitingSelection, ownsFulfillment: true);

    /// <summary>A release is queued at the download client.</summary>
    public static readonly AcquisitionStatusDefinition Queued = new(AcquisitionStatus.Queued, ownsFulfillment: true);

    /// <summary>The download client is transferring the release.</summary>
    public static readonly AcquisitionStatusDefinition Downloading = new(AcquisitionStatus.Downloading, ownsFulfillment: true);

    /// <summary>The download is paused because its client is unavailable.</summary>
    public static readonly AcquisitionStatusDefinition WaitingForDownloadClient = new(AcquisitionStatus.WaitingForDownloadClient, ownsFulfillment: true);

    /// <summary>The payload is ready to import.</summary>
    public static readonly AcquisitionStatusDefinition Downloaded = new(AcquisitionStatus.Downloaded, ownsFulfillment: true);

    /// <summary>The payload is being moved into its library.</summary>
    public static readonly AcquisitionStatusDefinition Importing = new(AcquisitionStatus.Importing, ownsFulfillment: true);

    /// <summary>The payload was placed and imported; the acquisition no longer owns its scope.</summary>
    public static readonly AcquisitionStatusDefinition Imported = new(AcquisitionStatus.Imported, ownsFulfillment: false);

    /// <summary>A destructive workflow claimed the acquisition and is tearing it down.</summary>
    public static readonly AcquisitionStatusDefinition Stopping = new(AcquisitionStatus.Stopping, ownsFulfillment: true);

    /// <summary>The acquisition failed and can be retried, so it keeps its scope.</summary>
    public static readonly AcquisitionStatusDefinition Failed = new(AcquisitionStatus.Failed, ownsFulfillment: true);

    /// <summary>The acquisition was cancelled; it no longer owns its scope.</summary>
    public static readonly AcquisitionStatusDefinition Cancelled = new(AcquisitionStatus.Cancelled, ownsFulfillment: false);

    /// <summary>The payload needs manual import resolution.</summary>
    public static readonly AcquisitionStatusDefinition ManualImportRequired = new(AcquisitionStatus.ManualImportRequired, ownsFulfillment: true);

    /// <summary>Every status definition, one per <see cref="AcquisitionStatus"/> member.</summary>
    public static IReadOnlyList<AcquisitionStatusDefinition> All { get; } = [
        Pending,
        WaitingForRelease,
        ManualSearchRequired,
        Searching,
        AwaitingSelection,
        Queued,
        Downloading,
        WaitingForDownloadClient,
        Downloaded,
        Importing,
        Imported,
        Stopping,
        Failed,
        Cancelled,
        ManualImportRequired
    ];

    /// <summary>Statuses whose acquisition owns its work and scope against every other fulfillment owner.</summary>
    public static IReadOnlyList<AcquisitionStatus> OwningFulfillment { get; } =
        All.Where(definition => definition.OwnsFulfillment).Select(definition => definition.Status).ToArray();

    #endregion

    #region Variables

    /// <summary>Persisted and generated identity of this status.</summary>
    public AcquisitionStatus Status { get; }

    /// <summary>Whether an acquisition in this status owns its work and scope against other fulfillment owners.</summary>
    public bool OwnsFulfillment { get; }

    #endregion

    #region Constructors

    private AcquisitionStatusDefinition(AcquisitionStatus status, bool ownsFulfillment) {
        Status = status;
        OwnsFulfillment = ownsFulfillment;
    }

    #endregion

    #region Actions - Lookup

    /// <summary>Returns the definition of a persisted status.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined status.</exception>
    public static AcquisitionStatusDefinition For(AcquisitionStatus status) =>
        All.FirstOrDefault(definition => definition.Status == status)
        ?? throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown acquisition status.");

    #endregion
}
