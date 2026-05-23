namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Base class for every mutable behavior module that can be attached to an entity.
/// A capability owns its own data shape and the operations that mutate it; the owning
/// <see cref="Prismedia.Domain.Entities.Entity" /> composes capabilities but does not reach into them.
/// </summary>
public abstract class EntityCapability;
