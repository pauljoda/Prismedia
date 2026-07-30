namespace Prismedia.Contracts.Entities;

/// <summary>
/// Immutable document contract for a modular Entity capability. Transport serialization reads
/// the <see cref="CapabilityKindAttribute" /> declared on each subtype at application startup.
/// </summary>
public abstract record EntityCapability;
