using Prismedia.Domain.Entities;

namespace Prismedia.Application.Entities;

/// <summary>Repairs missing provider ordering without replacing positions already held by the library.</summary>
public interface IEntityPositionEnricher {
    /// <summary>
    /// Fills missing positions on the expected entity kind and reconciles its structural sort order.
    /// Existing canonical positions, including user corrections and unrelated orderings, are preserved.
    /// </summary>
    Task<EntityMetadataPatchResult> FillMissingPositionsAsync(
        Guid entityId,
        EntityKind expectedKind,
        IReadOnlyDictionary<string, int> positions,
        CancellationToken cancellationToken);
}
