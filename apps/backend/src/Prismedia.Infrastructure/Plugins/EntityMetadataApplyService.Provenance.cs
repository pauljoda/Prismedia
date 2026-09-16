using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class EntityMetadataApplyService {
    private async Task<IReadOnlySet<MetadataPatchField>> LockedScalarFieldsAsync(Guid entityId, CancellationToken cancellationToken) =>
        (await _db.EntityMetadataFields.Where(row => row.EntityId == entityId && row.IsLocked)
            .Select(row => row.Field).ToArrayAsync(cancellationToken)).ToHashSet();

    private async Task RecordScalarEvidenceAsync(Guid entityId, EntityMetadataPatch patch, IEnumerable<string> fields,
        EntityMetadataProposal? provider, DateTimeOffset now, CancellationToken cancellationToken) {
        var selected = fields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var field in MetadataFieldProtection.Fields.Where(field => selected.Contains(field.ToCode()))) {
            var value = field switch {
                MetadataPatchField.Title => patch.Title,
                MetadataPatchField.Description => patch.Description,
                MetadataPatchField.Classification => patch.Classification,
                _ => throw new InvalidOperationException("Unsupported scalar metadata field.")
            };
            if (provider is not null && string.IsNullOrWhiteSpace(value)) continue;
            var row = await _db.EntityMetadataFields.FindAsync([entityId, field], cancellationToken);
            var evidence = row?.Evidence() ?? MetadataFieldEvidence.Unknown;
            var next = provider is null
                ? evidence.WrittenByUser(string.IsNullOrWhiteSpace(value), now)
                : evidence.WrittenByProvider(provider.Provider, provider.Confidence, now);
            if (next == evidence) continue;
            if (row is null) {
                row = new EntityMetadataFieldRow { EntityId = entityId, Field = field };
                _db.EntityMetadataFields.Add(row);
            }
            row.Apply(next);
        }
    }

    private static EntityMetadataPatch PreserveLockedScalars(EntityMetadataPatch patch, IReadOnlySet<MetadataPatchField> locked) => patch with {
        Title = locked.Contains(MetadataPatchField.Title) ? null : patch.Title,
        Description = locked.Contains(MetadataPatchField.Description) ? null : patch.Description,
        Classification = locked.Contains(MetadataPatchField.Classification) ? null : patch.Classification
    };
}
