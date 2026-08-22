using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Media.Persistence;

public sealed partial class LibraryScanPersistenceService {
    // Taxonomy rows resolved during this job (tag/person/studio by kind + case-insensitive
    // title). Survives CompleteScanBatchAsync so 9,000 videos sharing 200 tags resolve each tag
    // once per job instead of once per video. Values are detached snapshots; only their ids and
    // kind codes feed new relationship rows.
    private readonly Dictionary<(string KindCode, string TitleLower), TaxonomyRef> _taxonomyByTitle = [];

    private sealed record TaxonomyRef(Guid Id, string KindCode, string Title) {
        public bool IsNsfw { get; set; }
    }

    /// <inheritdoc cref="IScanMetadataPersistence.CompleteScanBatchAsync" />
    public Task CompleteScanBatchAsync(CancellationToken cancellationToken) {
        // The scan shares one job-lifetime context; accumulated tracked rows made change
        // detection and DbSet.Local scans progressively slower across a large pass (throughput
        // measurably decayed by the tail of a 9k-file scan). Batches save before calling this.
        _db.ChangeTracker.Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc cref="IScanMetadataPersistence.ApplyVideoSidecarMetadataBatchAsync" />
    public async Task ApplyVideoSidecarMetadataBatchAsync(
        IReadOnlyList<VideoSidecarApplyItem> items,
        CancellationToken cancellationToken) {
        if (items.Count == 0) {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var ids = items.Select(item => item.EntityId).Distinct().ToArray();
        var entitiesById = await _db.Entities
            .Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var descriptionsById = await _db.EntityDescriptions
            .Where(row => ids.Contains(row.EntityId))
            .ToDictionaryAsync(row => row.EntityId, cancellationToken);
        var releaseCode = EntityDateType.Release.ToCode();
        var datesById = await _db.EntityDates
            .Where(row => ids.Contains(row.EntityId) && row.Code == releaseCode)
            .ToDictionaryAsync(row => row.EntityId, cancellationToken);
        var urlRows = await _db.EntityUrls.AsNoTracking()
            .Where(row => ids.Contains(row.EntityId))
            .Select(row => new { row.EntityId, row.Url, row.SortOrder })
            .ToArrayAsync(cancellationToken);
        var urlsByEntity = urlRows
            .GroupBy(row => row.EntityId)
            .ToDictionary(
                group => group.Key,
                group => (
                    Seen: new HashSet<string>(group.Select(row => row.Url), StringComparer.OrdinalIgnoreCase),
                    NextOrder: group.Count()));
        var tagsCode = RelationshipKind.Tags.ToCode();
        var castCode = RelationshipKind.Cast.ToCode();
        var studioCode = RelationshipKind.Studio.ToCode();
        string[] linkCodes = [tagsCode, castCode, studioCode];
        var linkRows = await _db.EntityRelationshipLinks.AsNoTracking()
            .Where(row => ids.Contains(row.EntityId) && linkCodes.Contains(row.RelationshipCode))
            .Select(row => new { row.EntityId, row.RelationshipCode, row.TargetEntityId, row.SortOrder })
            .ToArrayAsync(cancellationToken);
        var linksByEntity = linkRows
            .GroupBy(row => (row.EntityId, row.RelationshipCode))
            .ToDictionary(
                group => group.Key,
                group => (
                    Targets: group.Select(row => row.TargetEntityId).ToHashSet(),
                    NextOrder: group.Max(row => row.SortOrder) + 1));

        await ResolveTaxonomyBatchAsync(items, now, cancellationToken);

        foreach (var item in items) {
            if (!entitiesById.TryGetValue(item.EntityId, out var entity)) {
                continue;
            }

            var changed = false;
            var metadata = item.Metadata;

            if (!string.IsNullOrWhiteSpace(metadata.Title) &&
                entity.Title.Equals(item.FallbackTitle, StringComparison.OrdinalIgnoreCase) &&
                !entity.Title.Equals(metadata.Title.Trim(), StringComparison.Ordinal)) {
                entity.Title = metadata.Title.Trim();
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(metadata.Description)) {
                if (!descriptionsById.TryGetValue(item.EntityId, out var description)) {
                    description = new EntityDescriptionRow {
                        EntityId = item.EntityId,
                        Value = metadata.Description.Trim(),
                        UpdatedAt = now
                    };
                    _db.EntityDescriptions.Add(description);
                    descriptionsById[item.EntityId] = description;
                    changed = true;
                } else if (string.IsNullOrWhiteSpace(description.Value)) {
                    description.Value = metadata.Description.Trim();
                    description.UpdatedAt = now;
                    changed = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(metadata.Date)) {
                var trimmed = metadata.Date.Trim();
                DateOnly? sortable = DateOnly.TryParse(trimmed, out var parsed) ? parsed : null;
                if (!datesById.TryGetValue(item.EntityId, out var date)) {
                    date = new EntityDateRow {
                        EntityId = item.EntityId,
                        Code = releaseCode,
                        Value = trimmed,
                        SortableValue = sortable,
                        UpdatedAt = now
                    };
                    _db.EntityDates.Add(date);
                    datesById[item.EntityId] = date;
                    changed = true;
                } else if (string.IsNullOrWhiteSpace(date.Value)) {
                    date.Value = trimmed;
                    date.SortableValue = sortable;
                    date.UpdatedAt = now;
                    changed = true;
                }
            }

            if (metadata.Urls.Count > 0) {
                if (!urlsByEntity.TryGetValue(item.EntityId, out var urls)) {
                    urls = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
                }

                foreach (var url in Unique(metadata.Urls)) {
                    if (!urls.Seen.Add(url)) {
                        continue;
                    }

                    _db.EntityUrls.Add(new EntityUrlRow {
                        Id = Guid.NewGuid(),
                        EntityId = item.EntityId,
                        Url = url,
                        SortOrder = urls.NextOrder,
                        CreatedAt = now
                    });
                    urls.NextOrder++;
                    changed = true;
                }

                urlsByEntity[item.EntityId] = urls;
            }

            changed |= AddResolvedLinks(
                linksByEntity, item.EntityId, tagsCode, "Tags",
                Unique(metadata.Tags), EntityKind.Tag.ToCode(), metadataJson: null, now);

            if (!string.IsNullOrWhiteSpace(metadata.Studio) &&
                !linksByEntity.TryGetValue((item.EntityId, studioCode), out _)) {
                changed |= AddResolvedLinks(
                    linksByEntity, item.EntityId, studioCode, "Studio",
                    [metadata.Studio.Trim()], EntityKind.Studio.ToCode(), metadataJson: null, now);
            }

            var actorCode = CreditRole.Actor.ToCode();
            changed |= AddResolvedLinks(
                linksByEntity, item.EntityId, castCode, "Cast",
                Unique(metadata.Performers), EntityKind.Person.ToCode(),
                $$"""{"role":"{{actorCode}}","roles":["{{actorCode}}"]}""", now);

            if (changed) {
                entity.UpdatedAt = now;
            }
        }

        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves every taxonomy title the batch references through the per-job cache, loading and
    /// creating the misses in one query per kind instead of one per name per video.
    /// </summary>
    private async Task ResolveTaxonomyBatchAsync(
        IReadOnlyList<VideoSidecarApplyItem> items,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var wanted = new Dictionary<(string KindCode, string TitleLower), (string Title, bool MarkNsfw)>();
        void Want(string kindCode, IEnumerable<string?> titles, bool markNsfw) {
            foreach (var title in Unique(titles)) {
                var key = (kindCode, title.ToLowerInvariant());
                if (wanted.TryGetValue(key, out var existing)) {
                    if (markNsfw && !existing.MarkNsfw) {
                        wanted[key] = (existing.Title, true);
                    }
                } else if (!_taxonomyByTitle.ContainsKey(key)) {
                    wanted[key] = (title, markNsfw);
                } else if (markNsfw) {
                    wanted[key] = (title, true);
                }
            }
        }

        foreach (var item in items) {
            Want(EntityKind.Tag.ToCode(), item.Metadata.Tags, item.MarkNsfw);
            Want(EntityKind.Person.ToCode(), item.Metadata.Performers, item.MarkNsfw);
            if (!string.IsNullOrWhiteSpace(item.Metadata.Studio)) {
                Want(EntityKind.Studio.ToCode(), [item.Metadata.Studio], item.MarkNsfw);
            }
        }

        foreach (var kindGroup in wanted.GroupBy(pair => pair.Key.KindCode)) {
            var kindCode = kindGroup.Key;
            var lowerTitles = kindGroup.Select(pair => pair.Key.TitleLower).ToArray();
            var found = await _db.Entities
                .Where(row => row.KindCode == kindCode && lowerTitles.Contains(row.Title.ToLower()))
                .ToArrayAsync(cancellationToken);
            var foundByLower = found
                .GroupBy(row => row.Title.ToLowerInvariant())
                .ToDictionary(group => group.Key, group => group.First());

            foreach (var pair in kindGroup) {
                var key = pair.Key;
                if (_taxonomyByTitle.TryGetValue(key, out var cached)) {
                    if (pair.Value.MarkNsfw && !cached.IsNsfw) {
                        await MarkTaxonomyNsfwAsync(cached, now, cancellationToken);
                    }
                    continue;
                }

                if (foundByLower.TryGetValue(key.TitleLower, out var row)) {
                    if (pair.Value.MarkNsfw && !row.IsNsfw) {
                        row.IsNsfw = true;
                        row.UpdatedAt = now;
                    }

                    _taxonomyByTitle[key] = new TaxonomyRef(row.Id, row.KindCode, row.Title) { IsNsfw = row.IsNsfw };
                    continue;
                }

                var created = new EntityRow {
                    Id = Guid.NewGuid(),
                    KindCode = kindCode,
                    Title = pair.Value.Title,
                    IsNsfw = pair.Value.MarkNsfw,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.Entities.Add(created);
                _taxonomyByTitle[key] = new TaxonomyRef(created.Id, created.KindCode, created.Title) {
                    IsNsfw = created.IsNsfw
                };
            }
        }
    }

    private async Task MarkTaxonomyNsfwAsync(TaxonomyRef cached, DateTimeOffset now, CancellationToken cancellationToken) {
        var row = await _db.Entities.FirstOrDefaultAsync(entity => entity.Id == cached.Id, cancellationToken);
        if (row is not null && !row.IsNsfw) {
            row.IsNsfw = true;
            row.UpdatedAt = now;
        }

        cached.IsNsfw = true;
    }

    private bool AddResolvedLinks(
        Dictionary<(Guid EntityId, string RelationshipCode), (HashSet<Guid> Targets, int NextOrder)> linksByEntity,
        Guid entityId,
        string relationshipCode,
        string label,
        IReadOnlyList<string> titles,
        string targetKindCode,
        string? metadataJson,
        DateTimeOffset now) {
        if (titles.Count == 0) {
            return false;
        }

        var changed = false;
        if (!linksByEntity.TryGetValue((entityId, relationshipCode), out var links)) {
            links = ([], 0);
        }

        foreach (var title in titles) {
            if (!_taxonomyByTitle.TryGetValue((targetKindCode, title.ToLowerInvariant()), out var target) ||
                !links.Targets.Add(target.Id)) {
                continue;
            }

            _db.EntityRelationshipLinks.Add(new EntityRelationshipLinkRow {
                EntityId = entityId,
                RelationshipCode = relationshipCode,
                Label = label,
                TargetEntityId = target.Id,
                TargetKindCode = target.KindCode,
                SortOrder = links.NextOrder,
                MetadataJson = metadataJson,
                CreatedAt = now
            });
            links.NextOrder++;
            changed = true;
        }

        linksByEntity[(entityId, relationshipCode)] = links;
        return changed;
    }

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
        await UpsertDateIfMissingAsync(
            entityId,
            EntityDateType.Release.ToCode(),
            metadata.Date,
            now,
            cancellationToken);
        await AddUrlsAsync(entityId, metadata.Urls, now, cancellationToken);
        await AddTagsAsync(entityId, metadata.Tags, now, markNsfw, cancellationToken);
        await SetStudioIfMissingAsync(entityId, metadata.Studio, now, markNsfw, cancellationToken);
        await AddCreditsAsync(entityId, metadata.Performers, CreditRole.Actor, now, markNsfw, cancellationToken);

        entity.UpdatedAt = now;
        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    public async Task ApplyComicInfoMetadataAsync(
        Guid entityId,
        ComicInfoMetadata metadata,
        bool markNsfw,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var entity = await _db.Entities.FirstOrDefaultAsync(row => row.Id == entityId, cancellationToken);
        if (entity is null) {
            return;
        }

        await UpsertDescriptionIfMissingAsync(entityId, metadata.Summary, now, cancellationToken);
        await UpsertDateIfMissingAsync(
            entityId,
            EntityDateType.Release.ToCode(),
            metadata.Date,
            now,
            cancellationToken);
        await AddUrlsAsync(entityId, metadata.Urls, now, cancellationToken);
        await AddTagsAsync(entityId, metadata.Tags, now, markNsfw, cancellationToken);
        await SetStudioIfMissingAsync(entityId, metadata.Publisher, now, markNsfw, cancellationToken);
        await AddCreditsAsync(entityId, metadata.Creators, CreditRole.Creator, now, markNsfw, cancellationToken);

        if (markNsfw && !entity.IsNsfw) {
            entity.IsNsfw = true;
        }

        entity.UpdatedAt = now;
        await SaveChangesWithLifecycleAsync(cancellationToken);
    }

    public async Task ApplyBookFileMetadataAsync(
        Guid entityId,
        BookFileMetadata metadata,
        bool markNsfw,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var entity = await _db.Entities.FirstOrDefaultAsync(
            row => row.Id == entityId && row.KindCode == EntityKind.Book.ToCode(),
            cancellationToken);
        if (entity is null) {
            return;
        }

        await UpsertDescriptionIfMissingAsync(entityId, metadata.Summary, now, cancellationToken);
        await AddCreditsAsync(entityId, metadata.Creators, CreditRole.Creator, now, markNsfw, cancellationToken);
        if (metadata.PageCount is { } pageCount) {
            await EntityPageCountPersistence.SetAsync(_db, entityId, pageCount, cancellationToken);
        }
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
            var tag = await FindOrCreateTaxonomyEntityAsync(EntityKind.Tag.ToCode(), name, now, markNsfw, cancellationToken);
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

        var studio = await FindOrCreateTaxonomyEntityAsync(EntityKind.Studio.ToCode(), studioName.Trim(), now, markNsfw, cancellationToken);
        AddRelationship(entityId, studioCode, "Studio", studio, 0, null, now);
    }

    private async Task AddCreditsAsync(
        Guid entityId,
        IReadOnlyList<string> names,
        CreditRole role,
        DateTimeOffset now,
        bool markNsfw,
        CancellationToken cancellationToken) {
        var castCode = RelationshipKind.Cast.ToCode();
        var roleCode = role.ToCode();
        var order = await NextRelationshipSortOrderAsync(entityId, castCode, cancellationToken);
        foreach (var name in Unique(names)) {
            var person = await FindOrCreateTaxonomyEntityAsync(EntityKind.Person.ToCode(), name, now, markNsfw, cancellationToken);
            if (await RelationshipExistsAsync(entityId, castCode, person.Id, cancellationToken)) {
                continue;
            }

            AddRelationship(
                entityId,
                castCode,
                "Cast",
                person,
                order++,
                $$"""{"role":"{{roleCode}}","roles":["{{roleCode}}"]}""",
                now);
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
