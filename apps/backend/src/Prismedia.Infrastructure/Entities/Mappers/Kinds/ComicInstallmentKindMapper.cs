using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Kinds;

internal sealed class ComicInstallmentKindMapper(PrismediaDbContext db) : IEntityKindMapper {
    public EntityKind Kind => EntityKind.ComicInstallment;

    public async Task<Entity> ConstructAsync(EntityRow row, CancellationToken cancellationToken) {
        var detail = await db.ComicInstallmentDetails.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.EntityId == row.Id, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Comic installment Entity '{row.Id}' is missing its required detail row.");
        return new ComicInstallment(
            row.Id,
            row.Title,
            detail.InstallmentKind,
            row.ParentEntityId,
            sortOrder: row.SortOrder);
    }
}
