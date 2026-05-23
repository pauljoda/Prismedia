namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable date capability for named provider or user-facing dates.
/// Holds the shared <see cref="EntityDate" /> value object, which is also used by
/// <see cref="CapabilityLifetime" />.
/// </summary>
public sealed class CapabilityDates(IEnumerable<EntityDate>? items = null)
    : CollectionCapability<EntityDate>(items) {
    /// <summary>Sets a named date, replacing any existing value for the same code.</summary>
    /// <param name="code">Stable date code.</param>
    /// <param name="value">Original display value.</param>
    /// <param name="sortableValue">Optional normalized sortable date.</param>
    /// <param name="precision">Optional precision code.</param>
    public void Set(string code, string value, DateOnly? sortableValue = null, string? precision = null) {
        RemoveItems(item => string.Equals(item.Code, code, StringComparison.Ordinal));
        AddItem(new EntityDate(code, value, sortableValue, precision));
    }
}
