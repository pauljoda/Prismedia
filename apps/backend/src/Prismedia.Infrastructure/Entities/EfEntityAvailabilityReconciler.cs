using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities;

/// <summary>PostgreSQL adapter for the persisted Entity availability self-heal projection.</summary>
public sealed class EfEntityAvailabilityReconciler(PrismediaDbContext db)
    : IEntityAvailabilityReconciler {
    /// <inheritdoc />
    public async Task<int> ReconcileAsync(CancellationToken cancellationToken) {
        if (!db.Database.IsNpgsql()) {
            return 0;
        }

        return await db.Database
            .SqlQueryRaw<int>(
                "SELECT prismedia_reconcile_entity_availability() AS \"Value\"")
            .SingleAsync(cancellationToken);
    }
}
