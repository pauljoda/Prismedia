using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable non-time progress capability for page, chapter, and other unit-based flows. The main
/// cursor is the work's single last-used position; kinds that declare consumption modalities also
/// keep one exact <see cref="ProgressCheckpoint"/> per modality beside it.
/// </summary>
public sealed class CapabilityProgress : EntityCapability {
    #region Variables

    private readonly Dictionary<ConsumptionModality, ProgressCheckpoint> _checkpoints = [];

    /// <summary>Exact last position of each consumption modality, independent of the main cursor.</summary>
    public IReadOnlyDictionary<ConsumptionModality, ProgressCheckpoint> Checkpoints => _checkpoints;

    /// <summary>Entity addressed by the main cursor, such as a chapter or the work itself.</summary>
    public Guid? CurrentEntityId { get; private set; }

    /// <summary>Unit counted by the main cursor.</summary>
    public ProgressUnit Unit { get; private set; }

    /// <summary>Zero-based main-cursor position within <see cref="Total"/>.</summary>
    public int Index { get; private set; }

    /// <summary>Unit total addressed by the main cursor.</summary>
    public int Total { get; private set; }

    /// <summary>Reader layout recorded with the main cursor.</summary>
    public ReaderMode? Mode { get; private set; }

    /// <summary>Time of the accepted completion signal, or null while unfinished.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Time of the latest accepted main-cursor signal.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>
    /// Number of units consumed independently from the current cursor. This lets the current
    /// location move backward while completion/coverage percentage remains meaningful.
    /// </summary>
    public int ConsumedCount { get; private set; }

    /// <summary>
    /// Opaque format-specific resume locator (e.g. an EPUB CFI). Null for unit-only
    /// progress such as comic page indexes where <see cref="Index"/> fully describes position.
    /// </summary>
    public string? Location { get; private set; }

    /// <summary>
    /// Modality of the newest checkpoint, or null without checkpoints. Equal timestamps resolve to
    /// the modality listed first in <see cref="ConsumptionModalityDefinition.All"/>.
    /// </summary>
    public ConsumptionModality? LastModality => LastCheckpoint?.Modality;

    /// <summary>
    /// The newest checkpoint when it is still worth resuming: null without checkpoints, or when the
    /// work's completion was accepted at or after that checkpoint.
    /// </summary>
    public ProgressCheckpoint? Resumable => LastCheckpoint is { } last && IsResumable(last) ? last : null;

    private ProgressCheckpoint? LastCheckpoint {
        get {
            ProgressCheckpoint? newest = null;
            foreach (var definition in ConsumptionModalityDefinition.All) {
                if (_checkpoints.TryGetValue(definition.Modality, out var checkpoint) &&
                    (newest is null || checkpoint.UpdatedAt > newest.UpdatedAt)) {
                    newest = checkpoint;
                }
            }
            return newest;
        }
    }

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a progress capability.
    /// </summary>
    /// <exception cref="ArgumentException">Two checkpoints share one modality.</exception>
    public CapabilityProgress(
        Guid? currentEntityId = null,
        ProgressUnit unit = ProgressUnit.Item,
        int index = 0,
        int total = 0,
        ReaderMode? mode = null,
        DateTimeOffset? completedAt = null,
        DateTimeOffset? updatedAt = null,
        string? location = null,
        int consumedCount = 0,
        IEnumerable<ProgressCheckpoint>? checkpoints = null) {
        CurrentEntityId = currentEntityId;
        Unit = unit;
        Index = index;
        Total = total;
        Mode = mode;
        CompletedAt = completedAt;
        UpdatedAt = updatedAt;
        Location = location;
        ConsumedCount = completedAt is not null && total > 0
            ? total
            : consumedCount > 0
                ? consumedCount
                : currentEntityId is not null && total > 0
                    ? Math.Clamp(index + 1, 0, total)
                    : 0;
        foreach (var checkpoint in checkpoints ?? []) {
            if (!_checkpoints.TryAdd(checkpoint.Modality, checkpoint)) {
                throw new ArgumentException(
                    $"Progress can keep only one {checkpoint.Modality.ToCode()} checkpoint.",
                    nameof(checkpoints));
            }
        }
    }

    #endregion

    #region Actions - Checkpoints

    /// <summary>Returns the exact checkpoint of <paramref name="modality"/>, if one was recorded.</summary>
    public ProgressCheckpoint? CheckpointFor(ConsumptionModality modality) =>
        _checkpoints.GetValueOrDefault(modality);

    /// <summary>
    /// Whether <paramref name="checkpoint"/> is worth resuming: it was recorded after the work's
    /// accepted completion, or the work is unfinished. The signal that completes a work shares its
    /// timestamp, so that position resumes as a fresh start.
    /// </summary>
    public bool IsResumable(ProgressCheckpoint checkpoint) =>
        CompletedAt is not { } completedAt || checkpoint.UpdatedAt > completedAt;

    #endregion

    #region Mutators

    /// <summary>
    /// Keeps <paramref name="checkpoint"/> as its modality's exact position unless that modality
    /// already holds a newer one. An equal timestamp is accepted; other modalities are never touched.
    /// </summary>
    /// <returns><see langword="true"/> when the checkpoint was recorded.</returns>
    public bool TryRecord(ProgressCheckpoint checkpoint) {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (_checkpoints.TryGetValue(checkpoint.Modality, out var existing) &&
            checkpoint.UpdatedAt < existing.UpdatedAt) {
            return false;
        }

        _checkpoints[checkpoint.Modality] = checkpoint;
        return true;
    }

    /// <summary>
    /// Moves the cursor only when this is not an older reading-progress signal.
    /// Optionally marks the same accepted signal as completed.
    /// </summary>
    /// <returns><see langword="true"/> when the cursor was updated.</returns>
    public bool TryMoveTo(
        Guid currentEntityId,
        ProgressUnit unit,
        int index,
        int total,
        ReaderMode? mode,
        DateTimeOffset updatedAt,
        string? location = null,
        bool completed = false,
        int? consumedCount = null) {
        if (!AcceptsProgressSignal(updatedAt)) {
            return false;
        }

        CurrentEntityId = currentEntityId;
        Unit = unit;
        Index = index;
        Total = total;
        Mode = mode;
        Location = location;
        UpdatedAt = updatedAt;
        if (consumedCount is { } value) {
            ConsumedCount = Math.Max(0, value);
        } else if (total > 0) {
            ConsumedCount = Math.Max(ConsumedCount, Math.Clamp(index + 1, 0, total));
        }
        if (completed) {
            CompletedAt = updatedAt;
            ConsumedCount = Math.Max(ConsumedCount, Math.Max(0, consumedCount ?? total));
        }
        return true;
    }

    /// <summary>
    /// Clears completion only when this is not an older reading-progress signal.
    /// </summary>
    /// <returns><see langword="true"/> when the completion state was cleared.</returns>
    public bool TryMarkIncomplete(DateTimeOffset updatedAt) {
        if (!AcceptsProgressSignal(updatedAt)) {
            return false;
        }

        CompletedAt = null;
        UpdatedAt = updatedAt;
        return true;
    }

    /// <summary>
    /// Marks the work finished without moving the cursor, for a completing signal whose position
    /// does not place the cursor (for example audio outside any paired chapter).
    /// </summary>
    /// <param name="updatedAt">Server time at which the completing signal was accepted.</param>
    /// <param name="raisesCoverage">
    /// Whether completion also fills the cursor's consumed coverage. A work that keeps reading and
    /// listening separate passes <see langword="false"/> for a listening completion, because its
    /// coverage measures reading alone.
    /// </param>
    /// <returns><see langword="true"/> when completion was recorded.</returns>
    public bool TryMarkCompleted(DateTimeOffset updatedAt, bool raisesCoverage = true) {
        if (!AcceptsProgressSignal(updatedAt)) {
            return false;
        }

        CompletedAt = updatedAt;
        UpdatedAt = updatedAt;
        if (raisesCoverage) {
            ConsumedCount = Math.Max(ConsumedCount, Math.Max(0, Total));
        }
        return true;
    }

    /// <summary>
    /// Starts consumed coverage and completion over without moving the cursor, for a start-over
    /// signal whose position does not place the cursor.
    /// </summary>
    /// <returns><see langword="true"/> when coverage was reset.</returns>
    public bool TryResetCoverage(DateTimeOffset updatedAt) {
        if (!AcceptsProgressSignal(updatedAt)) {
            return false;
        }

        CompletedAt = null;
        ConsumedCount = 0;
        UpdatedAt = updatedAt;
        return true;
    }

    private bool AcceptsProgressSignal(DateTimeOffset updatedAt) =>
        UpdatedAt is null || updatedAt >= UpdatedAt;

    #endregion
}
