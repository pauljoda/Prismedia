using Microsoft.Extensions.Logging;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// One event to append to the durable acquisition activity log — the write-side shape of an
/// <see cref="Contracts.Acquisition.AcquisitionHistoryView"/>, minus the store-assigned id and timestamp.
/// Every field beyond the event, title, and kind is best-effort context: the writer supplies as much as
/// is cheaply on hand at the call site (the release title, indexer, client, detected quality) and leaves
/// the rest null.
/// </summary>
/// <param name="AcquisitionId">The acquisition the event belongs to; null when it is already gone or unresolvable.</param>
/// <param name="EntityId">The library entity the acquisition targeted, denormalized so per-entity history survives the acquisition's deletion.</param>
/// <param name="Kind">Media kind the event concerns.</param>
/// <param name="Event">Which lifecycle event this records.</param>
/// <param name="Title">The acquisition's display title at the time of the event.</param>
/// <param name="ReleaseTitle">The release title the event concerned, when known.</param>
/// <param name="IndexerName">Indexer the release came from, when known.</param>
/// <param name="DownloadClientName">Download client the release was handed to, when known.</param>
/// <param name="QualityCode">Detected quality code (ladder code or book rank) at the event, when known.</param>
/// <param name="FormatScore">Total custom-format score of the release, when computed.</param>
/// <param name="Message">Human-readable detail (a failure reason, an old→new upgrade summary), when relevant.</param>
public sealed record AcquisitionHistoryEntry(
    Guid? AcquisitionId,
    Guid? EntityId,
    EntityKind Kind,
    AcquisitionHistoryEvent Event,
    string Title,
    string? ReleaseTitle = null,
    string? IndexerName = null,
    string? DownloadClientName = null,
    string? QualityCode = null,
    int? FormatScore = null,
    string? Message = null);

/// <summary>
/// Persistence port for the durable acquisition activity log. The log is append-only and OUTLIVES the
/// acquisitions it describes (the acquisition FK nulls on delete), giving Prismedia the same permanent
/// grabbed/imported/failed/removed audit trail Sonarr keeps. Writes are best-effort — see
/// <see cref="SafeAddAsync"/> — so recording history never fails the operation being recorded.
/// </summary>
public interface IAcquisitionHistoryStore {
    /// <summary>Appends one event to the log.</summary>
    Task AddAsync(AcquisitionHistoryEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Lists log entries newest-first. <paramref name="limit"/> is clamped to the range 1..500 (a
    /// non-positive value falls back to the default of 200). <paramref name="entityId"/> optionally
    /// restricts the log to one library entity's events.
    /// </summary>
    Task<IReadOnlyList<AcquisitionHistoryView>> ListAsync(int limit, Guid? entityId, CancellationToken cancellationToken);
}

/// <summary>Best-effort helpers over <see cref="IAcquisitionHistoryStore"/>.</summary>
public static class AcquisitionHistoryStoreExtensions {
    /// <summary>
    /// The default page size for <see cref="IAcquisitionHistoryStore.ListAsync"/> when no positive limit is given.
    /// </summary>
    public const int DefaultListLimit = 200;

    /// <summary>The largest page <see cref="IAcquisitionHistoryStore.ListAsync"/> will return.</summary>
    public const int MaxListLimit = 500;

    /// <summary>
    /// Appends a history event, swallowing (and logging) any failure. The activity log is a secondary
    /// record: a store hiccup while writing it must never fail — or roll back — the grab/import/removal it
    /// is recording. Every event write on a hot path goes through this so the "history is best-effort"
    /// rule lives in one place instead of a try/catch scattered per call site.
    /// </summary>
    public static async Task SafeAddAsync(
        this IAcquisitionHistoryStore store,
        ILogger logger,
        AcquisitionHistoryEntry entry,
        CancellationToken cancellationToken) {
        try {
            await store.AddAsync(entry, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to record acquisition history event {Event} for {AcquisitionId}.", entry.Event, entry.AcquisitionId);
        }
    }
}
