using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers;

/// <summary>
/// Per-capability persistence mapper. One implementation per domain capability owns the
/// row-shape, hydrate, and persist behavior for that capability so <see cref="EfEntityRepository"/>
/// can stay a coordinator over a discovered list of mappers. Adding a new capability means
/// adding one mapper next to the row class, not editing the repository.
/// </summary>
public interface IEntityCapabilityMapper {
    /// <summary>
    /// Loads the capability's persistent state from its row(s) and attaches it to the
    /// hydrated <paramref name="entity"/>. No-op when the entity has no row for this
    /// capability.
    /// </summary>
    Task HydrateAsync(Entity entity, CancellationToken cancellationToken);

    /// <summary>
    /// Queues removal of all stale rows owned by this mapper for <paramref name="entity"/>.
    /// The repository invokes <see cref="ClearAsync"/> for every mapper, flushes the
    /// removals in one <c>SaveChanges</c>, and then calls <see cref="PersistAsync"/> to
    /// queue the new rows. Splitting clear from persist keeps both phases idempotent on
    /// EntityId-keyed rows without per-mapper saves and works on the EF Core InMemory
    /// provider used by tests.
    /// </summary>
    Task ClearAsync(Entity entity, CancellationToken cancellationToken);

    /// <summary>
    /// Queues the capability's current state on <paramref name="entity"/> as new rows.
    /// </summary>
    Task PersistAsync(Entity entity, CancellationToken cancellationToken);
}
