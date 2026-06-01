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
        string unit = "item",
        int index = 0,
        int total = 0,
        string? mode = null,
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
    public string Unit { get; private set; }
    public int Index { get; private set; }
    public int Total { get; private set; }
    public string? Mode { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>
    /// Opaque format-specific resume locator (e.g. an EPUB CFI). Null for unit-only
    /// progress such as comic page indexes where <see cref="Index"/> fully describes position.
    /// </summary>
    public string? Location { get; private set; }

    /// <summary>Moves the progress cursor to a specific entity and index.</summary>
    public void MoveTo(Guid currentEntityId, string unit, int index, int total, string? mode, DateTimeOffset updatedAt, string? location = null) {
        CurrentEntityId = currentEntityId;
        Unit = unit;
        Index = index;
        Total = total;
        Mode = mode;
        Location = location;
        CompletedAt = null;
        UpdatedAt = updatedAt;
    }

    /// <summary>Marks the progress as completed.</summary>
    public void MarkCompleted(DateTimeOffset completedAt) {
        CompletedAt = completedAt;
        UpdatedAt = completedAt;
    }

    /// <summary>Clears the completion flag while leaving the current position untouched.</summary>
    public void MarkIncomplete(DateTimeOffset updatedAt) {
        CompletedAt = null;
        UpdatedAt = updatedAt;
    }
}
