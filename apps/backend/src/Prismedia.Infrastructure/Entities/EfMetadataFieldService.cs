using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>Persists metadata provenance protection under the same lifecycle boundary as metadata application.</summary>
public sealed class EfMetadataFieldService(PrismediaDbContext db, IEntityLifecycleMutationLease lifecycle) : IMetadataFieldService {
    /// <inheritdoc />
    public async Task<IReadOnlyList<MetadataFieldResponse>?> ReadAsync(Guid entityId, CancellationToken cancellationToken) {
        if (!await db.Entities.AnyAsync(entity => entity.Id == entityId, cancellationToken)) return null;
        var rows = await db.EntityMetadataFields.AsNoTracking().Where(row => row.EntityId == entityId).ToArrayAsync(cancellationToken);
        return MetadataFieldProtection.Fields.Select(field => Response(field, rows.FirstOrDefault(row => row.Field == field)?.Evidence() ?? MetadataFieldEvidence.Unknown)).ToArray();
    }
    /// <inheritdoc />
    public async Task<MetadataFieldResponse?> SetLockAsync(Guid entityId, MetadataPatchField field, UpdateMetadataFieldLockRequest request, CancellationToken cancellationToken) {
        if (!MetadataFieldProtection.Supports(field) || request.ExpectedRevision < 0) throw new ArgumentException("Choose a supported scalar field and its current revision.");
        MetadataFieldResponse? result = null;
        try {
            var accepted = await lifecycle.ExecuteAsync(entityId, async token => {
                if (!await db.Entities.AnyAsync(entity => entity.Id == entityId, token)) return;
                var row = await db.EntityMetadataFields.FindAsync([entityId, field], token);
                var evidence = row?.Evidence() ?? MetadataFieldEvidence.Unknown;
                if (evidence.Revision != request.ExpectedRevision) throw new MetadataFieldConflictException();
                var next = evidence.WithLock(request.IsLocked);
                if (next != evidence) {
                    if (row is null) { row = new EntityMetadataFieldRow { EntityId = entityId, Field = field }; db.EntityMetadataFields.Add(row); }
                    row.Apply(next);
                    await db.SaveChangesAsync(token);
                }
                result = Response(field, next);
            }, cancellationToken);
            return accepted ? result : null;
        } catch (DbUpdateConcurrencyException) { throw new MetadataFieldConflictException(); }
    }
    private static MetadataFieldResponse Response(MetadataPatchField field, MetadataFieldEvidence evidence) => new(field,
        evidence.Origin, evidence.ProviderId, evidence.ObservedAt, evidence.Confidence, evidence.IsCleared, evidence.IsLocked, evidence.Revision);
}
