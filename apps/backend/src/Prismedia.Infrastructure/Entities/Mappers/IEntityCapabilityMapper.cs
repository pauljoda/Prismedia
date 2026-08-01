using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers;

/// <summary>
/// Per-capability hydration mapper. One implementation per domain capability owns its row
/// shape and domain reconstruction so <see cref="EfEntityRepository"/> can stay a coordinator
/// over a discovered list of mappers.
/// </summary>
public interface IEntityCapabilityMapper {
    /// <summary>
    /// Loads the capability's persistent state from its row(s) and attaches it to the
    /// hydrated <paramref name="entity"/>. No-op when the entity has no row for this
    /// capability.
    /// </summary>
    Task HydrateAsync(Entity entity, CancellationToken cancellationToken);

}

/// <summary>
/// Persistence mapper for one explicitly mutable capability. Unlike hydration mappers, these
/// are invoked only when the application names their <see cref="CapabilityType"/> in a mutation; they
/// must upsert their own rows and must not clear unrelated capability state.
/// </summary>
public interface IEntityMutableStateMapper {
    /// <summary>Exact domain capability type this mapper owns.</summary>
    Type CapabilityType { get; }

    /// <summary>Persists the selected mutable capability state for <paramref name="entity"/>.</summary>
    Task PersistAsync(Entity entity, CancellationToken cancellationToken);
}

/// <summary>
/// Strongly typed mutable-capability mapper contract. Implementing this interface declares the
/// owned capability type directly, so registration never needs a parallel type property or list.
/// </summary>
/// <typeparam name="TCapability">Domain capability whose mutable rows the mapper owns.</typeparam>
public interface IEntityMutableStateMapper<TCapability> : IEntityMutableStateMapper
    where TCapability : EntityCapability {
    Type IEntityMutableStateMapper.CapabilityType => typeof(TCapability);
}
