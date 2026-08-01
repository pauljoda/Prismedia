using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

/// <summary>
/// Persistence mapper for the <see cref="CapabilityLifetime"/> capability. Stores the
/// semantic lifetime range (start/end dates plus label) as a single row per entity in the
/// <c>entity_lifetimes</c> table.
/// </summary>
internal sealed class LifetimeCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var row = await db.EntityLifetimes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EntityId == entity.Id, cancellationToken);
        if (row is null) {
            return;
        }

        entity.RemoveCapability<CapabilityLifetime>();
        entity.AddCapability(new CapabilityLifetime(
            start: ToEntityDate(row.StartCode, row.StartValue, row.StartSortableValue, row.StartPrecision),
            end: ToEntityDate(row.EndCode, row.EndValue, row.EndSortableValue, row.EndPrecision),
            label: row.Label));
    }

    /// <summary>
    /// Reconstructs an <see cref="EntityDate"/> from its flattened column values.
    /// Returns null when the value column is empty, meaning no date was stored.
    /// </summary>
    private static EntityDate? ToEntityDate(
        string? code,
        string? value,
        DateOnly? sortableValue,
        string? precision) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : new EntityDate(code ?? string.Empty, value, sortableValue, precision);
}
