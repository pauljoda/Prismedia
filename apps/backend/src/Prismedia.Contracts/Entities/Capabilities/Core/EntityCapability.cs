namespace Prismedia.Contracts.Entities;

/// <summary>
/// Base API contract for a modular entity capability.
/// JSON polymorphism discriminators are wired up at startup by
/// <see cref="CapabilityPolymorphism.ConfigureEntityCapabilityPolymorphism" />,
/// reading the <see cref="CapabilityKindAttribute" /> declared on each subtype.
/// </summary>
public abstract record EntityCapability;
