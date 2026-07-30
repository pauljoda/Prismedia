namespace Prismedia.Contracts.Entities;

/// <summary>
/// Declares the JSON discriminator value for an <see cref="EntityCapability" /> subtype.
/// A startup resolver scans for this attribute to populate the polymorphism options on
/// <see cref="EntityCapability" />, so each capability owns its discriminator locally
/// instead of relying on a centralized <c>JsonDerivedType</c> chain.
/// </summary>
/// <param name="Kind">Stable discriminator string written into the API JSON envelope.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CapabilityKindAttribute(string kind) : Attribute {
    /// <summary>Stable discriminator string written into the API JSON envelope.</summary>
    public string Kind { get; } = kind;
}
