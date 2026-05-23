namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable description capability for entities that expose synopsis, overview, notes, or details text.
/// </summary>
public sealed class CapabilityDescription : EntityCapability {
    /// <summary>
    /// Creates a description capability.
    /// </summary>
    /// <param name="value">User-facing description text.</param>
    public CapabilityDescription(string value = "") {
        Value = value;
    }

    /// <inheritdoc />

    /// <summary>User-facing description text.</summary>
    public string Value { get; private set; }

    /// <summary>Updates the description text.</summary>
    /// <param name="value">Replacement description text.</param>
    public void SetValue(string value) {
        Value = value;
    }
}
