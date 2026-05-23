using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers;

/// <summary>
/// Per-kind persistence + projection mapper. One implementation per <see cref="EntityKind"/>
/// owns the concrete domain constructor wiring, per-kind detail row read/write, and the
/// kind-specific detail contract projection so <see cref="EfEntityRepository"/> and
/// <see cref="EfEntityReadService"/> can stay coordinators over a discovered set of mappers.
/// Adding a new kind means adding one mapper next to the row, not editing the repository or
/// the read service.
/// </summary>
public interface IEntityKindMapper {
    /// <summary>Entity kind handled by this mapper.</summary>
    EntityKind Kind { get; }

    /// <summary>
    /// Builds the concrete <see cref="Entity"/> for this kind from the loaded
    /// <paramref name="row"/>, reading any kind-specific detail row as needed.
    /// </summary>
    Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the kind-specific detail row(s) for <paramref name="entity"/>. No-op when
    /// the kind has no detail table.
    /// </summary>
    Task PersistDetailAsync(Entity entity, CancellationToken cancellationToken);

    /// <summary>
    /// Projects the hydrated <paramref name="entity"/> into its kind-specific detail
    /// contract, using the already-projected shared <paramref name="card"/> and the
    /// person credit metadata projection. Kinds without their own detail contract
    /// return the bare <paramref name="card"/>.
    /// </summary>
    IEntityCard ProjectDetail(
        Entity entity,
        EntityCard card,
        IReadOnlyList<EntityCreditMetadata> creditMetadata);
}
