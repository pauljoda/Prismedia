using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers;

/// <summary>
/// Per-kind projection mapper. One implementation per <see cref="EntityKind"/>
/// owns the concrete domain constructor wiring and per-kind detail row reads so
/// <see cref="EfEntityRepository"/> can stay a coordinator over a discovered set of mappers.
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

}
