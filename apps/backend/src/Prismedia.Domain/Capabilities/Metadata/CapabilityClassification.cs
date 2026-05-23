namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable classification capability for provider or user-facing ratings and certifications.
/// </summary>
public sealed class CapabilityClassification : EntityCapability {
    /// <summary>
    /// Creates a classification capability.
    /// </summary>
    /// <param name="value">Classification value.</param>
    /// <param name="system">Optional classification system or provider code.</param>
    public CapabilityClassification(string? value = null, string? system = null) {
        Value = value;
        System = system;
    }

    /// <inheritdoc />

    /// <summary>Classification value, such as a content rating or certification.</summary>
    public string? Value { get; private set; }

    /// <summary>Optional classification system or provider code.</summary>
    public string? System { get; private set; }

    /// <summary>Updates the classification values.</summary>
    public void Set(string? value, string? system = null) {
        Value = value;
        System = system;
    }
}
