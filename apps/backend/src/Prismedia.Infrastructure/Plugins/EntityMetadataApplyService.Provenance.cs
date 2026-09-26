using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class EntityMetadataApplyService {
    private async Task<IReadOnlySet<MetadataPatchField>> LockedMetadataFieldsAsync(Guid entityId, CancellationToken cancellationToken) =>
        (await _db.EntityMetadataFields.Where(row => row.EntityId == entityId && row.IsLocked)
            .Select(row => row.Field).ToArrayAsync(cancellationToken)).ToHashSet();

    private async Task RecordMetadataEvidenceAsync(Guid entityId, EntityMetadataPatch patch, IEnumerable<string> fields,
        EntityMetadataProposal? provider, DateTimeOffset now, CancellationToken cancellationToken) {
        var selected = fields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var field in MetadataFieldProtection.Fields.Where(field => selected.Contains(field.ToCode()))) {
            var cleared = field switch {
                MetadataPatchField.Title => string.IsNullOrWhiteSpace(patch.Title),
                MetadataPatchField.Description => string.IsNullOrWhiteSpace(patch.Description),
                MetadataPatchField.Classification => string.IsNullOrWhiteSpace(patch.Classification),
                MetadataPatchField.Credits => patch.Credits.Count == 0,
                _ => throw new InvalidOperationException("Unsupported protected metadata field.")
            };
            if (provider is not null && cleared) continue;
            var row = await _db.EntityMetadataFields.FindAsync([entityId, field], cancellationToken);
            var evidence = row?.Evidence() ?? MetadataFieldEvidence.Unknown;
            var next = provider is null
                ? evidence.WrittenByUser(cleared, now)
                : evidence.WrittenByProvider(provider.Provider, provider.Confidence, now);
            if (next == evidence) continue;
            if (row is null) {
                row = new EntityMetadataFieldRow { EntityId = entityId, Field = field };
                _db.EntityMetadataFields.Add(row);
            }
            row.Apply(next);
        }
    }

    private static EntityMetadataPatch PreserveLockedMetadata(EntityMetadataPatch patch, IReadOnlySet<MetadataPatchField> locked) => patch with {
        Title = locked.Contains(MetadataPatchField.Title) ? null : patch.Title,
        Description = locked.Contains(MetadataPatchField.Description) ? null : patch.Description,
        Classification = locked.Contains(MetadataPatchField.Classification) ? null : patch.Classification,
        Credits = locked.Contains(MetadataPatchField.Credits) ? [] : patch.Credits
    };
}
