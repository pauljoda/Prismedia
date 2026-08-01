using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable non-time progress capability for page, chapter, and other unit-based flows.
/// </summary>
public sealed class CapabilityProgress : EntityCapability {
    /// <summary>
    /// Creates a progress capability.
    /// </summary>
    public CapabilityProgress(
        Guid? currentEntityId = null,
        ProgressUnit unit = ProgressUnit.Item,
        int index = 0,
        int total = 0,
        ReaderMode? mode = null,
        DateTimeOffset? completedAt = null,
        DateTimeOffset? updatedAt = null,
        string? location = null) {
        CurrentEntityId = currentEntityId;
        Unit = unit;
        Index = index;
        Total = total;
        Mode = mode;
        CompletedAt = completedAt;
        UpdatedAt = updatedAt;
        Location = location;
    }

    /// <inheritdoc />

    public Guid? CurrentEntityId { get; private set; }
    public ProgressUnit Unit { get; private set; }
    public int Index { get; private set; }
    public int Total { get; private set; }
    public ReaderMode? Mode { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>
    /// Opaque format-specific resume locator (e.g. an EPUB CFI). Null for unit-only
    /// progress such as comic page indexes where <see cref="Index"/> fully describes position.
    /// </summary>
    public string? Location { get; private set; }

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
        bool completed = false) {
        if (!AcceptsProgressSignal(updatedAt)) {
            return false;
        }

        CurrentEntityId = currentEntityId;
        Unit = unit;
        Index = index;
        Total = total;
        Mode = mode;
        Location = location;
        CompletedAt = null;
        UpdatedAt = updatedAt;
        if (completed) {
            CompletedAt = updatedAt;
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

    private bool AcceptsProgressSignal(DateTimeOffset updatedAt) =>
        UpdatedAt is null || updatedAt >= UpdatedAt;
}
