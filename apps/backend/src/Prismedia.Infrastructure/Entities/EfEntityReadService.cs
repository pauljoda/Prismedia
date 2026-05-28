using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Entities.Mappers;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// EF Core adapter for <see cref="IEntityReadService"/>. Card and detail reads flow
/// through the hydrated domain entity and <see cref="EntityCardProjector"/>; the
/// browse and thumbnail path stays a deliberate row-optimized projection so list
/// pages do not pay the full hydration cost. Kind-specific detail DTO projection is
/// delegated to <see cref="IEntityKindMapper.ProjectDetail"/> so this service stays a
/// coordinator and never branches on a concrete entity kind.
/// </summary>
public sealed class EfEntityReadService : IEntityReadService {
    private const int DefaultPageSize = 250;
    private const int MaxPageSize = 1000;
    private const int MaxHoverImages = 5;
    private const int MaxHoverImageSearchDepth = 3;
    private const int MaxThumbnailMeta = 5;

    private readonly PrismediaDbContext _db;
    private readonly EfEntityRepository _repository;
    private readonly IReadOnlyDictionary<EntityKind, IEntityKindMapper> _kindMappers;

    public EfEntityReadService(
        PrismediaDbContext db,
        EfEntityRepository repository,
        IEnumerable<IEntityKindMapper> kindMappers) {
        _db = db;
        _repository = repository;
        _kindMappers = kindMappers.ToDictionary(mapper => mapper.Kind);
    }

    public async Task<EntityListResponse> ListAsync(
        string? kind,
        string? query,
        string? cursor,
        bool? hideNsfw,
        int? limit,
        CancellationToken cancellationToken,
        Guid? referencedBy = null,
        string? relationshipCode = null) {
        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var normalizedRelationshipCode = string.IsNullOrWhiteSpace(relationshipCode)
            ? null
            : relationshipCode.Trim();
        var entityQuery = _db.Entities.AsNoTracking()
            .Where(entity => entity.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(kind)) {
            var kindCode = kind.Trim();
            entityQuery = entityQuery.Where(entity => entity.KindCode == kindCode);
            if (ListBrowseShowsOnlyTopLevel(kindCode)) {
                entityQuery = entityQuery.Where(entity => entity.ParentEntityId == null);
            }
        }

        if (!string.IsNullOrWhiteSpace(query)) {
            var normalized = query.Trim().ToLower();
            entityQuery = entityQuery.Where(entity => entity.Title.ToLower().Contains(normalized));
        }

        if (referencedBy is { } targetEntityId) {
            entityQuery = entityQuery.Where(entity =>
                _db.EntityRelationshipLinks.Any(link =>
                    link.TargetEntityId == targetEntityId &&
                    link.EntityId == entity.Id &&
                    (normalizedRelationshipCode == null || link.RelationshipCode == normalizedRelationshipCode)));
        }

        entityQuery = ApplyNsfwVisibility(entityQuery, hideNsfw == true);

        // Snapshot the unbounded filtered total before applying the cursor; this is what
        // drives the client's page-of-pages and seek-to-end behaviour and must stay
        // independent of where in the cursor sequence we currently are.
        var totalCount = await entityQuery.CountAsync(cancellationToken);

        if (TryDecodeCursor(cursor, out var cursorTitle, out var cursorId)) {
            entityQuery = entityQuery.Where(entity =>
                string.Compare(entity.Title, cursorTitle) > 0 ||
                (entity.Title == cursorTitle && entity.Id.CompareTo(cursorId) > 0));
        }

        var rows = await entityQuery
            .OrderBy(entity => entity.Title)
            .ThenBy(entity => entity.Id)
            .Take(pageSize + 1)
            .ToArrayAsync(cancellationToken);

        var page = rows.Take(pageSize).ToArray();
        var thumbnails = await ProjectThumbnailsAsync(page, hideNsfw == true, cancellationToken);
        var nextCursor = rows.Length > pageSize ? EncodeCursor(page[^1].Title, page[^1].Id) : null;
        return new EntityListResponse(thumbnails, nextCursor, totalCount);
    }

    public async Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) {
        if (hideNsfw && await IsEntityHiddenAsync(id, cancellationToken)) {
            return null;
        }

        var entity = await _repository.FindShallowAsync(id, cancellationToken);
        if (entity is null) {
            return null;
        }

        var card = EntityCardProjector.ToCard(entity) with {
            ChildrenByKind = await ProjectDirectChildGroupsAsync(id, hideNsfw, cancellationToken),
            Relationships = await ProjectRelationshipGroupsAsync(id, hideNsfw, cancellationToken)
        };
        return await EnrichBookProgressAsync(card, hideNsfw, cancellationToken);
    }

    public async Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
        IReadOnlyList<Guid> ids,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var query = _db.Entities.AsNoTracking()
            .Where(entity => ids.Contains(entity.Id) && entity.DeletedAt == null);
        query = ApplyNsfwVisibility(query, hideNsfw);
        var rows = await query
            .ToArrayAsync(cancellationToken);
        var thumbnails = await ProjectThumbnailsAsync(rows, hideNsfw, cancellationToken);
        var byId = thumbnails.ToDictionary(item => item.Id);
        return new EntityThumbnailBatchResponse(ids.Where(byId.ContainsKey).Select(id => byId[id]).ToArray());
    }

    public async Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) {
        if (hideNsfw && await IsEntityHiddenAsync(id, cancellationToken)) {
            return null;
        }

        var entity = await _repository.FindShallowAsync(id, cancellationToken);
        if (entity is null) {
            return null;
        }

        var card = await EnrichBookProgressAsync(EntityCardProjector.ToCard(entity) with {
            ChildrenByKind = await ProjectDirectChildGroupsAsync(id, hideNsfw, cancellationToken),
            Relationships = await ProjectRelationshipGroupsAsync(id, hideNsfw, cancellationToken)
        }, hideNsfw, cancellationToken);
        if (!card.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var creditMetadata = await ProjectCreditMetadataAsync(id, hideNsfw, cancellationToken);
        return _kindMappers.TryGetValue(entity.Kind, out var mapper)
            ? mapper.ProjectDetail(entity, card, creditMetadata)
            : card;
    }

    private IQueryable<EntityRow> ApplyNsfwVisibility(IQueryable<EntityRow> query, bool hideNsfw) =>
        hideNsfw
            ? query.Where(entity => !entity.IsNsfw)
            : query;

    private static bool ListBrowseShowsOnlyTopLevel(string kind) =>
        kind.Equals(EntityKindRegistry.Gallery.Code, StringComparison.OrdinalIgnoreCase) ||
        kind.Equals(EntityKindRegistry.AudioLibrary.Code, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> IsEntityHiddenAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Entities.AsNoTracking()
            .AnyAsync(entity => entity.Id == id && entity.IsNsfw, cancellationToken);

    private async Task<EntityCard> EnrichBookProgressAsync(
        EntityCard card,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (card.Kind != EntityKindRegistry.Book.Code) {
            return card;
        }

        var progress = card.Capabilities.OfType<ProgressCapability>().FirstOrDefault();
        if (progress?.CurrentEntityId is not { } currentEntityId) {
            return card;
        }

        if (hideNsfw && await IsEntityHiddenAsync(currentEntityId, cancellationToken)) {
            return card with {
                Capabilities = card.Capabilities
                    .Where(capability => capability is not ProgressCapability)
                    .ToArray()
            };
        }

        var position = await _repository.ResolveBookProgressPositionAsync(
            card.Id,
            currentEntityId,
            progress.Index,
            progress.Total,
            cancellationToken);
        if (position is null) {
            return card;
        }

        return card with {
            Capabilities = card.Capabilities.Select(capability =>
                capability is ProgressCapability progressCapability
                    ? progressCapability with {
                        WorkIndex = position.Index,
                        WorkTotal = position.Total,
                    }
                    : capability).ToArray()
        };
    }

    private async Task<IReadOnlyList<EntityGroup>> ProjectDirectChildGroupsAsync(
        Guid entityId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var query = _db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == entityId && row.DeletedAt == null);
        query = ApplyNsfwVisibility(query, hideNsfw);
        var childRows = await query
            .OrderBy(row => row.KindCode)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.Title)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        if (childRows.Length == 0) {
            return [];
        }

        var groups = new List<EntityGroup>();
        foreach (var group in childRows.GroupBy(row => row.KindCode)) {
            if (!group.Key.TryDecodeAs<EntityKind>(out var childKind)) {
                continue;
            }

            groups.Add(new EntityGroup(
                group.Key,
                EntityKindRegistry.Describe(childKind).GroupLabel,
                await ProjectThumbnailsAsync(group.ToArray(), hideNsfw, cancellationToken)));
        }

        return groups;
    }

    private async Task<IReadOnlyList<EntityThumbnail>> ProjectThumbnailsAsync(
        IReadOnlyList<EntityRow> rows,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (rows.Count == 0) {
            return [];
        }

        var ids = rows.Select(entity => entity.Id).ToArray();
        var coverByEntity = await LoadCoverPathsAsync(ids, cancellationToken);
        var hoverFiles = await _db.EntityFiles.AsNoTracking()
            .Where(file => ids.Contains(file.EntityId) && file.Role == EntityFileRole.Trickplay)
            .Where(file => file.Path.EndsWith(".m3u8") || file.Path.EndsWith(".vtt"))
            .OrderByDescending(file => file.Path.EndsWith(".m3u8"))
            .ThenBy(file => file.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var hoverByEntity = hoverFiles
            .GroupBy(file => file.EntityId)
            .ToDictionary(group => group.Key, group => group.First().Path);
        var hoverImagesByEntity = await ProjectHoverImagesAsync(rows, hideNsfw, cancellationToken);
        var technicalByEntity = await _db.EntityTechnical.AsNoTracking()
            .Where(technical => ids.Contains(technical.EntityId))
            .ToDictionaryAsync(technical => technical.EntityId, cancellationToken);

        return rows.Select(row => {
            var hoverUrl = hoverByEntity.GetValueOrDefault(row.Id);
            var hoverImages = hoverImagesByEntity.GetValueOrDefault(row.Id) ?? [];
            var coverUrl = coverByEntity.GetValueOrDefault(row.Id);
            if (coverUrl is null && UsesRepresentativeCover(row.KindCode) && hoverImages.Count > 0) {
                coverUrl = hoverImages[0].Path;
            }

            return new EntityThumbnail(
                row.Id,
                row.KindCode,
                row.Title,
                row.ParentEntityId,
                row.SortOrder,
                coverUrl,
                hoverUrl is null ? "none" : "sprite",
                hoverUrl,
                hoverImages,
                ProjectThumbnailMeta(row, technicalByEntity.GetValueOrDefault(row.Id)),
                row.RatingValue,
                row.IsFavorite,
                row.IsNsfw,
                row.IsOrganized);
        }).ToArray();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadCoverPathsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) {
        if (ids.Count == 0) {
            return new Dictionary<Guid, string>();
        }

        var covers = await _db.EntityFiles.AsNoTracking()
            .Where(file => ids.Contains(file.EntityId))
            .Where(file => file.Role == EntityFileRole.Thumbnail ||
                file.Role == EntityFileRole.Poster ||
                file.Role == EntityFileRole.Cover ||
                file.Role == EntityFileRole.Logo ||
                file.Role == EntityFileRole.Backdrop)
            .ToArrayAsync(cancellationToken);

        return covers
            .GroupBy(file => file.EntityId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(file => CoverSourcePriority(file.Role, file.Source, file.Path))
                    .ThenBy(file => CoverRolePriority(file.Role))
                    .ThenBy(file => file.CreatedAt)
                    .First()
                    .Path);
    }

    private static int CoverRolePriority(EntityFileRole role) =>
        role switch {
            EntityFileRole.Thumbnail => 0,
            EntityFileRole.Poster => 1,
            EntityFileRole.Cover => 2,
            EntityFileRole.Logo => 3,
            _ => 4
        };

    private static int CoverSourcePriority(EntityFileRole role, string? source, string path) {
        if (role == EntityFileRole.Backdrop) {
            return 2;
        }

        return source == "custom" ||
            path.Contains("/custom/artwork/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/plugins/artwork/", StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1;
    }

    private static IReadOnlyList<EntityThumbnailMeta> ProjectThumbnailMeta(
        EntityRow row,
        EntityTechnicalRow? technical) {
        if (technical is null) {
            return [];
        }

        var meta = new List<EntityThumbnailMeta>(MaxThumbnailMeta);
        Add(meta, "duration", FormatDuration(technical.DurationSeconds));
        if (technical.Width is { } width && technical.Height is { } height) {
            Add(meta, row.KindCode == EntityKindRegistry.Video.Code ? "video" : "image", FormatResolution(width, height));
        }

        if (row.KindCode == EntityKindRegistry.Video.Code) {
            Add(meta, "video", technical.Codec?.ToUpperInvariant());
            Add(meta, "video", technical.Container?.ToUpperInvariant());
        } else if (row.KindCode == EntityKindRegistry.AudioTrack.Code) {
            Add(meta, "audio", technical.Codec?.ToUpperInvariant());
        }

        return meta.Take(MaxThumbnailMeta).ToArray();
    }

    private static void Add(List<EntityThumbnailMeta> meta, string icon, string? label) {
        if (!string.IsNullOrWhiteSpace(label)) {
            meta.Add(new EntityThumbnailMeta(icon, label));
        }
    }

    private static string? FormatDuration(double? seconds) {
        if (seconds is not { } value || !double.IsFinite(value) || value <= 0) {
            return null;
        }

        var duration = TimeSpan.FromSeconds(Math.Round(value));
        if (duration.TotalHours >= 1) {
            return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}";
        }

        return $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string FormatResolution(int width, int height) {
        if (height >= 2160) return "4K";
        if (height >= 1440) return "1440p";
        if (height >= 1080) return "1080p";
        if (height >= 720) return "720p";
        if (height >= 480) return "480p";
        return $"{width}x{height}";
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<EntityThumbnailHoverImage>>> ProjectHoverImagesAsync(
        IReadOnlyList<EntityRow> rows,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (rows.Count == 0) {
            return new Dictionary<Guid, IReadOnlyList<EntityThumbnailHoverImage>>();
        }

        var rootIds = rows.Select(row => row.Id).ToArray();
        var directChildQuery = _db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId != null && rootIds.Contains(row.ParentEntityId.Value) && row.DeletedAt == null);
        directChildQuery = ApplyNsfwVisibility(directChildQuery, hideNsfw);
        var directChildren = await directChildQuery
            .OrderBy(row => row.ParentEntityId)
            .ThenBy(row => row.SortOrder)
            .ThenBy(row => row.Title)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        if (directChildren.Length == 0) {
            return new Dictionary<Guid, IReadOnlyList<EntityThumbnailHoverImage>>();
        }

        var sampledByRoot = directChildren
            .GroupBy(row => row.ParentEntityId!.Value)
            .ToDictionary(group => group.Key, group => PickSpread(group.ToArray(), MaxHoverImages));
        var sampledChildren = sampledByRoot.Values.SelectMany(children => children).ToArray();
        var representatives = await ResolveRepresentativeHoverImagesAsync(sampledChildren, hideNsfw, cancellationToken);

        return sampledByRoot.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<EntityThumbnailHoverImage>)pair.Value
                .Select(child => representatives.GetValueOrDefault(child.Id))
                .Where(image => image is not null)
                .Select(image => image!)
                .ToArray());
    }

    private async Task<IReadOnlyDictionary<Guid, EntityThumbnailHoverImage>> ResolveRepresentativeHoverImagesAsync(
        IReadOnlyList<EntityRow> candidates,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (candidates.Count == 0) {
            return new Dictionary<Guid, EntityThumbnailHoverImage>();
        }

        var results = new Dictionary<Guid, EntityThumbnailHoverImage>();
        var chains = candidates.ToDictionary(row => row.Id, row => new List<EntityRow> { row });
        var frontier = candidates.ToDictionary(row => row.Id, row => row);

        for (var depth = 0; depth < MaxHoverImageSearchDepth && frontier.Count > 0; depth++) {
            var parentIds = frontier.Values.Select(row => row.Id).ToArray();
            var childQuery = _db.Entities.AsNoTracking()
                .Where(row => row.ParentEntityId != null && parentIds.Contains(row.ParentEntityId.Value) && row.DeletedAt == null);
            childQuery = ApplyNsfwVisibility(childQuery, hideNsfw);
            var children = await childQuery
                .OrderBy(row => row.ParentEntityId)
                .ThenBy(row => row.SortOrder)
                .ThenBy(row => row.Title)
                .ThenBy(row => row.Id)
                .ToArrayAsync(cancellationToken);
            if (children.Length == 0) {
                break;
            }

            var firstChildByParent = children
                .GroupBy(row => row.ParentEntityId!.Value)
                .ToDictionary(group => group.Key, group => group.First());
            var nextFrontier = new Dictionary<Guid, EntityRow>();
            foreach (var rootId in frontier.Keys.ToArray()) {
                var current = frontier[rootId];
                if (firstChildByParent.TryGetValue(current.Id, out var child)) {
                    chains[rootId].Add(child);
                    nextFrontier[rootId] = child;
                }
            }

            frontier = nextFrontier;
        }

        var coverByEntity = await LoadCoverPathsAsync(
            chains.Values.SelectMany(chain => chain).Select(row => row.Id).Distinct().ToArray(),
            cancellationToken);

        foreach (var pair in chains) {
            var representative = pair.Value.FirstOrDefault(row => coverByEntity.ContainsKey(row.Id));
            if (representative is not null) {
                results[pair.Key] = new EntityThumbnailHoverImage(
                    representative.Id,
                    representative.Title,
                    coverByEntity[representative.Id]);
            }
        }

        return results;
    }

    private static IReadOnlyList<T> PickSpread<T>(IReadOnlyList<T> items, int limit) {
        if (items.Count <= limit) {
            return items;
        }

        var selected = new List<T>(limit);
        var usedIndexes = new HashSet<int>();
        for (var index = 0; index < limit; index++) {
            var sourceIndex = (int)Math.Round(index * (items.Count - 1) / (double)(limit - 1));
            if (usedIndexes.Add(sourceIndex)) {
                selected.Add(items[sourceIndex]);
            }
        }

        return selected;
    }

    private static bool UsesRepresentativeCover(string kindCode) =>
        kindCode == EntityKindRegistry.Book.Code ||
        kindCode == EntityKindRegistry.BookVolume.Code ||
        kindCode == EntityKindRegistry.BookChapter.Code ||
        kindCode == EntityKindRegistry.Gallery.Code;

    private async Task<IReadOnlyList<EntityGroup>> ProjectRelationshipGroupsAsync(
        Guid entityId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var links = await _db.EntityRelationshipLinks.AsNoTracking()
            .Where(link => link.EntityId == entityId)
            .OrderBy(link => link.RelationshipCode)
            .ThenBy(link => link.SortOrder)
            .ThenBy(link => link.TargetEntityId)
            .ToArrayAsync(cancellationToken);
        if (links.Length == 0) {
            return [];
        }

        var targetIds = links.Select(link => link.TargetEntityId).Distinct().ToArray();
        var targetQuery = _db.Entities.AsNoTracking()
            .Where(entity => targetIds.Contains(entity.Id) && entity.DeletedAt == null);
        targetQuery = ApplyNsfwVisibility(targetQuery, hideNsfw);
        var targetRows = await targetQuery
            .ToDictionaryAsync(entity => entity.Id, cancellationToken);

        var groups = new List<EntityGroup>();
        foreach (var group in links.GroupBy(link => new { link.RelationshipCode, link.TargetKindCode })) {
            var orderedRows = group
                .Select(link => targetRows.GetValueOrDefault(link.TargetEntityId))
                .Where(row => row is not null)
                .Select(row => row!)
                .ToArray();
            if (orderedRows.Length == 0) {
                continue;
            }

            groups.Add(new EntityGroup(
                group.Key.TargetKindCode,
                RelationshipLabel(group.Key.RelationshipCode),
                await ProjectThumbnailsAsync(orderedRows, hideNsfw, cancellationToken)) {
                Code = group.Key.RelationshipCode
            });
        }

        return groups;
    }

    private async Task<IReadOnlyList<EntityCreditMetadata>> ProjectCreditMetadataAsync(
        Guid entityId,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var linksQuery = _db.EntityRelationshipLinks.AsNoTracking()
            .Where(link => link.EntityId == entityId &&
                           link.RelationshipCode == "cast" &&
                           link.TargetKindCode == EntityKindRegistry.Person.Code);
        if (hideNsfw) {
            linksQuery = linksQuery.Where(link =>
                !_db.Entities.Any(entity => entity.Id == link.TargetEntityId && entity.IsNsfw));
        }

        var links = await linksQuery
            .OrderBy(link => link.SortOrder)
            .ThenBy(link => link.TargetEntityId)
            .ToArrayAsync(cancellationToken);

        return links
            .Select(link => {
                var metadata = DecodeCreditMetadata(link.MetadataJson);
                return new EntityCreditMetadata(
                    link.TargetEntityId,
                    metadata.Role,
                    metadata.Character);
            })
            .ToArray();
    }

    private static (string? Role, string? Character) DecodeCreditMetadata(string? metadataJson) {
        if (string.IsNullOrWhiteSpace(metadataJson)) {
            return (null, null);
        }

        try {
            using var document = JsonDocument.Parse(metadataJson);
            var root = document.RootElement;
            return (
                TryGetString(root, "role"),
                TryGetString(root, "character"));
        } catch (JsonException) {
            return (null, null);
        }
    }

    private static string? TryGetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string RelationshipLabel(string code) =>
        code switch {
            "cast" => "Cast",
            "studio" => "Studios",
            "tags" => "Tags",
            "related" => "Related",
            _ => code.Replace('-', ' ')
        };

    private static string EncodeCursor(string title, Guid id) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{title}\n{id:N}"));

    private static bool TryDecodeCursor(string? cursor, out string title, out Guid id) {
        title = string.Empty;
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(cursor)) {
            return false;
        }

        try {
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('\n');
            return parts.Length == 2 && Guid.TryParseExact(parts[1], "N", out id) && !string.IsNullOrWhiteSpace(title = parts[0]);
        } catch (FormatException) {
            return false;
        }
    }
}
