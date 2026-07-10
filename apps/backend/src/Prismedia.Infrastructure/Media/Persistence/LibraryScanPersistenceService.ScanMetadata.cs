using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Media.Persistence;

public sealed partial class LibraryScanPersistenceService {
    public async Task ApplyVideoSidecarMetadataAsync(
        Guid entityId,
        VideoSidecarMetadata metadata,
        string fallbackTitle,
        bool markNsfw,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var entity = await _db.Entities.FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);
        if (entity is null) {
            return;
        }

        ApplyTitleIfScannedFallback(entity, metadata.Title, fallbackTitle, now);
        await UpsertDescriptionIfMissingAsync(entityId, metadata.Description, now, cancellationToken);
        await UpsertDateIfMissingAsync(entityId, "release", metadata.Date, now, cancellationToken);
        await AddUrlsAsync(entityId, metadata.Urls, now, cancellationToken);
        await AddTagsAsync(entityId, metadata.Tags, now, markNsfw, cancellationToken);
        await SetStudioIfMissingAsync(entityId, metadata.Studio, now, markNsfw, cancellationToken);
        await AddCreditsAsync(entityId, metadata.Performers, "performer", now, markNsfw, cancellationToken);

        entity.UpdatedAt = now;
        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    public async Task ApplyComicInfoMetadataAsync(
        Guid bookEntityId,
        ComicInfoMetadata metadata,
        bool markNsfw,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var entity = await _db.Entities.FirstOrDefaultAsync(row => row.Id == bookEntityId, cancellationToken);
        if (entity is null) {
            return;
        }

        await UpsertDescriptionIfMissingAsync(bookEntityId, metadata.Summary, now, cancellationToken);
        await UpsertDateIfMissingAsync(bookEntityId, "release", metadata.Date, now, cancellationToken);
        await AddUrlsAsync(bookEntityId, metadata.Urls, now, cancellationToken);
        await AddTagsAsync(bookEntityId, metadata.Tags, now, markNsfw, cancellationToken);
        await SetStudioIfMissingAsync(bookEntityId, metadata.Publisher, now, markNsfw, cancellationToken);
        await AddCreditsAsync(bookEntityId, metadata.Creators, "creator", now, markNsfw, cancellationToken);

        if (markNsfw && !entity.IsNsfw) {
            entity.IsNsfw = true;
        }

        entity.UpdatedAt = now;
        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    private static void ApplyTitleIfScannedFallback(
        EntityRow entity,
        string? title,
        string fallbackTitle,
        DateTimeOffset now) {
        if (string.IsNullOrWhiteSpace(title)) {
            return;
        }

        if (entity.Title.Equals(fallbackTitle, StringComparison.OrdinalIgnoreCase)) {
            entity.Title = title.Trim();
            entity.UpdatedAt = now;
        }
    }

    private async Task UpsertDescriptionIfMissingAsync(
        Guid entityId,
        string? value,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        var existing = await _db.EntityDescriptions.FindAsync([entityId], cancellationToken);
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.Value)) {
            return;
        }

        if (existing is null) {
            _db.EntityDescriptions.Add(new EntityDescriptionRow {
                EntityId = entityId,
                Value = value.Trim(),
                UpdatedAt = now
            });
        } else {
            existing.Value = value.Trim();
            existing.UpdatedAt = now;
        }
    }

    private async Task UpsertDateIfMissingAsync(
        Guid entityId,
        string code,
        string? value,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        var existing = await _db.EntityDates.FindAsync([entityId, code], cancellationToken);
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.Value)) {
            return;
        }

        var trimmed = value.Trim();
        DateOnly? sortable = DateOnly.TryParse(trimmed, out var parsed) ? parsed : (DateOnly?)null;
        if (existing is null) {
            _db.EntityDates.Add(new EntityDateRow {
                EntityId = entityId,
                Code = code,
                Value = trimmed,
                SortableValue = sortable,
                UpdatedAt = now
            });
        } else {
            existing.Value = trimmed;
            existing.SortableValue = sortable;
            existing.UpdatedAt = now;
        }
    }

    private async Task AddUrlsAsync(
        Guid entityId,
        IReadOnlyList<string> urls,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (urls.Count == 0) {
            return;
        }

        var existing = await _db.EntityUrls
            .Where(row => row.EntityId == entityId)
            .OrderBy(row => row.SortOrder)
            .Select(row => row.Url)
            .ToArrayAsync(cancellationToken);
        var seen = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sortOrder = existing.Length;

        foreach (var url in Unique(urls)) {
            if (!seen.Add(url)) {
                continue;
            }

            _db.EntityUrls.Add(new EntityUrlRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Url = url,
                SortOrder = sortOrder++,
                CreatedAt = now
            });
        }
    }

    private async Task AddTagsAsync(
        Guid entityId,
        IReadOnlyList<string> tags,
        DateTimeOffset now,
        bool markNsfw,
        CancellationToken cancellationToken) {
        var tagsCode = RelationshipKind.Tags.ToCode();
        var order = await NextRelationshipSortOrderAsync(entityId, tagsCode, cancellationToken);
        foreach (var name in Unique(tags)) {
            var tag = await FindOrCreateTaxonomyEntityAsync(EntityKindRegistry.Tag.Code, name, now, markNsfw, cancellationToken);
            if (await RelationshipExistsAsync(entityId, tagsCode, tag.Id, cancellationToken)) {
                continue;
            }

            AddRelationship(entityId, tagsCode, "Tags", tag, order++, null, now);
        }
    }

    private async Task SetStudioIfMissingAsync(
        Guid entityId,
        string? studioName,
        DateTimeOffset now,
        bool markNsfw,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(studioName)) {
            return;
        }

        var studioCode = RelationshipKind.Studio.ToCode();
        var hasStudio = await _db.EntityRelationshipLinks
            .AnyAsync(row => row.EntityId == entityId && row.RelationshipCode == studioCode, cancellationToken);
        if (hasStudio) {
            return;
        }

        var studio = await FindOrCreateTaxonomyEntityAsync(EntityKindRegistry.Studio.Code, studioName.Trim(), now, markNsfw, cancellationToken);
        AddRelationship(entityId, studioCode, "Studio", studio, 0, null, now);
    }

    private async Task AddCreditsAsync(
        Guid entityId,
        IReadOnlyList<string> names,
        string role,
        DateTimeOffset now,
        bool markNsfw,
        CancellationToken cancellationToken) {
        var castCode = RelationshipKind.Cast.ToCode();
        var order = await NextRelationshipSortOrderAsync(entityId, castCode, cancellationToken);
        foreach (var name in Unique(names)) {
            var person = await FindOrCreateTaxonomyEntityAsync(EntityKindRegistry.Person.Code, name, now, markNsfw, cancellationToken);
            if (await RelationshipExistsAsync(entityId, castCode, person.Id, cancellationToken)) {
                continue;
            }

            AddRelationship(entityId, castCode, "Cast", person, order++, $$"""{"role":"{{role}}","roles":["{{role}}"]}""", now);
        }
    }

    private async Task<EntityRow> FindOrCreateTaxonomyEntityAsync(
        string kindCode,
        string title,
        DateTimeOffset now,
        bool markNsfw,
        CancellationToken cancellationToken) {
        var entity = _db.Entities.Local.FirstOrDefault(row =>
                row.KindCode == kindCode &&
                row.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
            ?? await _db.Entities.FirstOrDefaultAsync(row =>
                row.KindCode == kindCode &&
                row.Title.ToLower() == title.ToLower(), cancellationToken);

        if (entity is null) {
            entity = new EntityRow {
                Id = Guid.NewGuid(),
                KindCode = kindCode,
                Title = title,
                IsNsfw = markNsfw,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Entities.Add(entity);
            return entity;
        }

        if (markNsfw && !entity.IsNsfw) {
            entity.IsNsfw = true;
            entity.UpdatedAt = now;
        }

        return entity;
    }

    private async Task<int> NextRelationshipSortOrderAsync(
        Guid entityId,
        string relationshipCode,
        CancellationToken cancellationToken) {
        var existing = await _db.EntityRelationshipLinks
            .Where(row => row.EntityId == entityId && row.RelationshipCode == relationshipCode)
            .Select(row => (int?)row.SortOrder)
            .MaxAsync(cancellationToken);

        return existing is null ? 0 : existing.Value + 1;
    }

    private async Task<bool> RelationshipExistsAsync(
        Guid entityId,
        string relationshipCode,
        Guid targetEntityId,
        CancellationToken cancellationToken) =>
        _db.EntityRelationshipLinks.Local.Any(row =>
            row.EntityId == entityId &&
            row.RelationshipCode == relationshipCode &&
            row.TargetEntityId == targetEntityId) ||
        await _db.EntityRelationshipLinks.AnyAsync(row =>
            row.EntityId == entityId &&
            row.RelationshipCode == relationshipCode &&
            row.TargetEntityId == targetEntityId, cancellationToken);

    private void AddRelationship(
        Guid entityId,
        string code,
        string label,
        EntityRow target,
        int sortOrder,
        string? metadataJson,
        DateTimeOffset now) =>
        _db.EntityRelationshipLinks.Add(new EntityRelationshipLinkRow {
            EntityId = entityId,
            RelationshipCode = code,
            Label = label,
            TargetEntityId = target.Id,
            TargetKindCode = target.KindCode,
            SortOrder = sortOrder,
            MetadataJson = metadataJson,
            CreatedAt = now
        });

    private static IReadOnlyList<string> Unique(IEnumerable<string?> values) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<string>();
        foreach (var value in values.Select(value => value?.Trim()).Where(value => !string.IsNullOrWhiteSpace(value))) {
            if (seen.Add(value!)) {
                output.Add(value!);
            }
        }

        return output;
    }
}
