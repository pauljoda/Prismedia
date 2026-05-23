namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable position capability for structural ordering and numbered media members.
/// </summary>
public sealed class CapabilityPosition(IEnumerable<CapabilityPosition.Item>? items = null)
    : CollectionCapability<CapabilityPosition.Item>(items) {
    /// <summary>
    /// Structural ordering or label value attached to an entity.
    /// </summary>
    /// <param name="Code">Stable position code, such as season, volume, chapter, page, track, or sort.</param>
    /// <param name="Value">Numeric position value.</param>
    /// <param name="Label">Optional display label when the numeric value is not enough for UI.</param>
    public sealed record Item(string Code, int Value, string? Label = null);

    /// <summary>Sets a position value, replacing any existing value for the same code.</summary>
    /// <param name="code">Stable position code.</param>
    /// <param name="value">Numeric position value.</param>
    /// <param name="label">Optional display label.</param>
    public void Set(string code, int value, string? label = null) {
        RemoveItems(item => string.Equals(item.Code, code, StringComparison.Ordinal));
        AddItem(new Item(code, value, label));
    }
}
