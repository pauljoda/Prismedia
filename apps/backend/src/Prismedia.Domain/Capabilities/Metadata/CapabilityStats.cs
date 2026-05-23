namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable statistics capability for stored or derived inventory counts.
/// </summary>
public sealed class CapabilityStats(IEnumerable<CapabilityStats.Item>? items = null)
    : CollectionCapability<CapabilityStats.Item>(items) {
    /// <summary>
    /// Named stored or derived inventory statistic attached to an entity.
    /// </summary>
    /// <param name="Code">Stable statistic code, such as images, tracks, pages, chapters, or items.</param>
    /// <param name="Value">Non-negative statistic value.</param>
    public sealed record Item(string Code, int Value);

    /// <summary>Sets a statistic value, replacing any existing value for the same code.</summary>
    /// <param name="code">Stable statistic code.</param>
    /// <param name="value">Non-negative statistic value.</param>
    public void Set(string code, int value) {
        RemoveItems(item => string.Equals(item.Code, code, StringComparison.Ordinal));
        AddItem(new Item(code, Math.Max(0, value)));
    }
}
