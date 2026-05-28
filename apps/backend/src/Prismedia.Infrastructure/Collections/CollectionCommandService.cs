using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Collections;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Collections;

/// <summary>
/// EF-backed collection command adapter. It owns collection-specific rows while using the
/// shared entity read service to return the same detail contracts as browse/detail routes.
/// </summary>
public sealed class CollectionCommandService(
    PrismediaDbContext db,
    IEntityReadService entities,
    ICollectionRuleEngine ruleEngine,
    ICollectionRefreshPersistence refreshPersistence) : ICollectionCommandService {
    private const int PreviewSampleSize = 24;
    private const string EmptyRuleJson = """{"type":"group","operator":"and","children":[]}""";

    private static readonly HashSet<string> SupportedItemKinds = new(StringComparer.OrdinalIgnoreCase) {
        EntityKindRegistry.Video.Code,
        EntityKindRegistry.VideoSeries.Code,
        EntityKindRegistry.Gallery.Code,
        EntityKindRegistry.Image.Code,
        EntityKindRegistry.Book.Code,
        EntityKindRegistry.AudioTrack.Code,
    };

    public async Task<CollectionWriteResult> CreateAsync(
        CollectionWriteRequest request,
        CancellationToken cancellationToken) {
        if (!TryNormalizeWrite(request, out var write, out var message)) {
            return InvalidWrite(message);
        }

        var now = DateTimeOffset.UtcNow;
        var collectionId = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = collectionId,
            KindCode = EntityKindRegistry.Collection.Code,
            Title = write.Title,
            IsNsfw = write.IsNsfw,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CollectionDetails.Add(new CollectionDetailRow {
            EntityId = collectionId,
            Mode = write.Mode,
            RuleTreeJson = write.RuleTreeJson,
            CoverMode = write.CoverMode,
            CoverItemEntityId = write.CoverItemId
        });
        await UpsertDescriptionAsync(collectionId, write.Description, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await RefreshRulesAfterWriteAsync(collectionId, write, cancellationToken);
        return await DetailResultAsync(collectionId, cancellationToken);
    }

    public async Task<CollectionWriteResult> UpdateAsync(
        Guid collectionId,
        CollectionWriteRequest request,
        CancellationToken cancellationToken) {
        if (!TryNormalizeWrite(request, out var write, out var message)) {
            return InvalidWrite(message);
        }

        var entity = await db.Entities
            .FirstOrDefaultAsync(row =>
                row.Id == collectionId &&
                row.KindCode == EntityKindRegistry.Collection.Code &&
                row.DeletedAt == null,
                cancellationToken);
        if (entity is null) {
            return new CollectionWriteResult(CollectionCommandStatus.NotFound, Message: "Collection was not found.");
        }

        var detail = await db.CollectionDetails.FindAsync([collectionId], cancellationToken)
            ?? TrackCollectionDetail(collectionId);
        var now = DateTimeOffset.UtcNow;
        entity.Title = write.Title;
        entity.IsNsfw = write.IsNsfw;
        entity.UpdatedAt = now;
        detail.Mode = write.Mode;
        detail.RuleTreeJson = write.RuleTreeJson;
        detail.CoverMode = write.CoverMode;
        detail.CoverItemEntityId = write.CoverItemId;
        await UpsertDescriptionAsync(collectionId, write.Description, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await RefreshRulesAfterWriteAsync(collectionId, write, cancellationToken);
        return await DetailResultAsync(collectionId, cancellationToken);
    }

    public async Task<CollectionCommandResult> DeleteAsync(
        Guid collectionId,
        CancellationToken cancellationToken) {
        var entity = await db.Entities
            .FirstOrDefaultAsync(row =>
                row.Id == collectionId &&
                row.KindCode == EntityKindRegistry.Collection.Code &&
                row.DeletedAt == null,
                cancellationToken);
        if (entity is null) {
            return new CollectionCommandResult(CollectionCommandStatus.NotFound, "Collection was not found.");
        }

        var now = DateTimeOffset.UtcNow;
        entity.DeletedAt = now;
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return new CollectionCommandResult(CollectionCommandStatus.Succeeded);
    }

    public async Task<CollectionCountResult> AddItemsAsync(
        Guid collectionId,
        CollectionAddItemsRequest request,
        CancellationToken cancellationToken) {
        var mode = await GetCollectionModeAsync(collectionId, cancellationToken);
        if (mode is null) {
            return new CollectionCountResult(CollectionCommandStatus.NotFound, Message: "Collection was not found.");
        }
        if (mode == CollectionMode.Dynamic) {
            return new CollectionCountResult(
                CollectionCommandStatus.Invalid,
                Message: "Dynamic collections are populated by rules.");
        }

        var references = DistinctReferences(request.Items ?? []);
        if (references.Count == 0) {
            return new CollectionCountResult(CollectionCommandStatus.Invalid, Message: "At least one item is required.");
        }

        var invalidKind = references.FirstOrDefault(reference => !SupportedItemKinds.Contains(reference.EntityType));
        if (invalidKind is not null) {
            return new CollectionCountResult(
                CollectionCommandStatus.Invalid,
                Message: $"Entity type '{invalidKind.EntityType}' cannot be added to a collection.");
        }

        var itemIds = references.Select(reference => reference.EntityId).ToArray();
        var activeItems = await db.Entities
            .Where(row => itemIds.Contains(row.Id) && row.DeletedAt == null)
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        foreach (var reference in references) {
            if (!activeItems.TryGetValue(reference.EntityId, out var row) ||
                !row.KindCode.Equals(reference.EntityType, StringComparison.OrdinalIgnoreCase)) {
                return new CollectionCountResult(
                    CollectionCommandStatus.Invalid,
                    Message: "One or more collection items were not found or had the wrong entity type.");
            }
        }

        var existingIds = await db.CollectionItemDetails
            .Where(row => row.CollectionEntityId == collectionId)
            .Select(row => row.ItemEntityId)
            .ToHashSetAsync(cancellationToken);
        var sortOrder = await NextSortOrderAsync(collectionId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var added = 0;
        foreach (var reference in references) {
            if (existingIds.Contains(reference.EntityId)) {
                continue;
            }

            db.CollectionItemDetails.Add(new CollectionItemDetailRow {
                Id = Guid.NewGuid(),
                CollectionEntityId = collectionId,
                ItemEntityId = reference.EntityId,
                Source = CollectionItemSource.Manual,
                SortOrder = sortOrder++,
                AddedAt = now,
            });
            existingIds.Add(reference.EntityId);
            added++;
        }

        await TouchCollectionAsync(collectionId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new CollectionCountResult(CollectionCommandStatus.Succeeded, added);
    }

    public async Task<CollectionCountResult> RemoveItemsAsync(
        Guid collectionId,
        CollectionRemoveItemsRequest request,
        CancellationToken cancellationToken) {
        var mode = await GetCollectionModeAsync(collectionId, cancellationToken);
        if (mode is null) {
            return new CollectionCountResult(CollectionCommandStatus.NotFound, Message: "Collection was not found.");
        }
        if (mode == CollectionMode.Dynamic) {
            return new CollectionCountResult(
                CollectionCommandStatus.Invalid,
                Message: "Dynamic collections are changed by refreshing their rules.");
        }

        var itemIds = (request.ItemIds ?? []).Distinct().ToArray();
        var rows = await db.CollectionItemDetails
            .Where(row => row.CollectionEntityId == collectionId && itemIds.Contains(row.Id))
            .ToArrayAsync(cancellationToken);
        db.CollectionItemDetails.RemoveRange(rows);
        await TouchCollectionAsync(collectionId, DateTimeOffset.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new CollectionCountResult(CollectionCommandStatus.Succeeded, rows.Length);
    }

    public async Task<CollectionCountResult> ReorderItemsAsync(
        Guid collectionId,
        CollectionReorderItemsRequest request,
        CancellationToken cancellationToken) {
        var mode = await GetCollectionModeAsync(collectionId, cancellationToken);
        if (mode is null) {
            return new CollectionCountResult(CollectionCommandStatus.NotFound, Message: "Collection was not found.");
        }
        if (mode == CollectionMode.Dynamic) {
            return new CollectionCountResult(
                CollectionCommandStatus.Invalid,
                Message: "Dynamic collections are changed by refreshing their rules.");
        }

        var rows = await db.CollectionItemDetails
            .Where(row => row.CollectionEntityId == collectionId)
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Id)
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0) {
            return new CollectionCountResult(CollectionCommandStatus.Succeeded, 0);
        }

        var requested = (request.ItemIds ?? []).Distinct().ToArray();
        var byId = rows.ToDictionary(row => row.Id);
        var ordered = requested
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .Concat(rows.Where(row => !requested.Contains(row.Id)))
            .ToArray();

        for (var i = 0; i < ordered.Length; i++) {
            ordered[i].SortOrder = i;
        }

        await TouchCollectionAsync(collectionId, DateTimeOffset.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new CollectionCountResult(CollectionCommandStatus.Succeeded, ordered.Length);
    }

    public async Task<CollectionRulePreviewResponse?> PreviewRulesAsync(
        CollectionRulePreviewRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (!ValidateRuleTree(request.RuleTreeJson, out _)) {
            return null;
        }

        var matches = await ruleEngine.EvaluateAsync(request.RuleTreeJson, cancellationToken);
        var visible = await VisibleMatchesAsync(matches, hideNsfw, cancellationToken);
        var counts = visible
            .GroupBy(match => match.EntityType)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var sampleIds = visible.Take(PreviewSampleSize).Select(match => match.EntityId).ToArray();
        var thumbnails = await entities.GetThumbnailsAsync(sampleIds, hideNsfw, cancellationToken);
        var thumbnailById = thumbnails.Items.ToDictionary(thumbnail => thumbnail.Id);
        var sample = visible
            .Take(PreviewSampleSize)
            .Select(match => thumbnailById.TryGetValue(match.EntityId, out var thumbnail)
                ? new CollectionRulePreviewItem(match.EntityType, match.EntityId, thumbnail)
                : null)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();

        return new CollectionRulePreviewResponse(visible.Count, counts, sample);
    }

    public async Task<CollectionRefreshResponse?> RefreshAsync(
        Guid collectionId,
        CancellationToken cancellationToken) {
        if (!await CollectionExistsAsync(collectionId, cancellationToken)) {
            return null;
        }

        var collection = await refreshPersistence.GetDynamicCollectionAsync(collectionId, cancellationToken);
        if (collection is null) {
            var currentCount = await CountItemsAsync(collectionId, cancellationToken);
            return new CollectionRefreshResponse(false, currentCount);
        }

        var matches = await ruleEngine.EvaluateAsync(collection.RuleTreeJson, cancellationToken);
        await refreshPersistence.RefreshCollectionItemsAsync(collectionId, matches, cancellationToken);
        return new CollectionRefreshResponse(true, await CountItemsAsync(collectionId, cancellationToken));
    }

    private async Task<CollectionWriteResult> DetailResultAsync(
        Guid collectionId,
        CancellationToken cancellationToken) {
        var collection = await entities.GetDetailAsync(
            collectionId,
            EntityKindRegistry.Collection.Code,
            hideNsfw: false,
            cancellationToken);
        return collection is CollectionDetail detail
            ? new CollectionWriteResult(CollectionCommandStatus.Succeeded, detail)
            : new CollectionWriteResult(CollectionCommandStatus.NotFound, Message: "Collection was not found after save.");
    }

    private static CollectionWriteResult InvalidWrite(string message) =>
        new(CollectionCommandStatus.Invalid, Message: message);

    private async Task RefreshRulesAfterWriteAsync(
        Guid collectionId,
        NormalizedCollectionWrite write,
        CancellationToken cancellationToken) {
        if (write.Mode is not (CollectionMode.Dynamic or CollectionMode.Hybrid) ||
            string.IsNullOrWhiteSpace(write.RuleTreeJson)) {
            return;
        }

        var matches = await ruleEngine.EvaluateAsync(write.RuleTreeJson, cancellationToken);
        await refreshPersistence.RefreshCollectionItemsAsync(collectionId, matches, cancellationToken);
    }

    private bool TryNormalizeWrite(
        CollectionWriteRequest request,
        out NormalizedCollectionWrite write,
        out string message) {
        write = default!;
        message = string.Empty;
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title)) {
            message = "Collection title is required.";
            return false;
        }

        var modeCode = string.IsNullOrWhiteSpace(request.Mode) ? CollectionMode.Manual.ToCode() : request.Mode.Trim();
        if (!modeCode.TryDecodeAs<CollectionMode>(out var mode)) {
            message = $"Unknown collection mode '{request.Mode}'.";
            return false;
        }

        var coverModeCode = string.IsNullOrWhiteSpace(request.CoverMode)
            ? CollectionCoverMode.Mosaic.ToCode()
            : request.CoverMode.Trim();
        if (!coverModeCode.TryDecodeAs<CollectionCoverMode>(out var coverMode)) {
            message = $"Unknown collection cover mode '{request.CoverMode}'.";
            return false;
        }

        var ruleTreeJson = NormalizeRuleTree(mode, request.RuleTreeJson);
        if (!ValidateRuleTree(ruleTreeJson, out message)) {
            return false;
        }

        write = new NormalizedCollectionWrite(
            title,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            mode,
            ruleTreeJson,
            coverMode,
            request.CoverItemId,
            request.IsNsfw ?? false);
        return true;
    }

    private static string? NormalizeRuleTree(CollectionMode mode, string? ruleTreeJson) {
        if (mode == CollectionMode.Manual) {
            return null;
        }

        return string.IsNullOrWhiteSpace(ruleTreeJson)
            ? EmptyRuleJson
            : ruleTreeJson.Trim();
    }

    private static bool ValidateRuleTree(string? ruleTreeJson, out string message) {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(ruleTreeJson)) {
            return true;
        }

        try {
            var node = JsonSerializer.Deserialize<CollectionRuleNode>(ruleTreeJson);
            if (node is CollectionRuleGroup) {
                return true;
            }

            message = "Collection rule tree must be a group node.";
            return false;
        } catch (JsonException) {
            message = "Collection rule tree is not valid JSON.";
            return false;
        } catch (NotSupportedException) {
            message = "Collection rule tree contains an unsupported node.";
            return false;
        }
    }

    private async Task UpsertDescriptionAsync(
        Guid collectionId,
        string? description,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var row = await db.EntityDescriptions.FindAsync([collectionId], cancellationToken);
        if (string.IsNullOrWhiteSpace(description)) {
            if (row is not null) {
                db.EntityDescriptions.Remove(row);
            }
            return;
        }

        if (row is null) {
            db.EntityDescriptions.Add(new EntityDescriptionRow {
                EntityId = collectionId,
                Value = description.Trim(),
                UpdatedAt = now,
            });
            return;
        }

        row.Value = description.Trim();
        row.UpdatedAt = now;
    }

    private async Task<bool> CollectionExistsAsync(Guid collectionId, CancellationToken cancellationToken) =>
        await db.Entities.AnyAsync(row =>
            row.Id == collectionId &&
            row.KindCode == EntityKindRegistry.Collection.Code &&
            row.DeletedAt == null,
            cancellationToken);

    private async Task<CollectionMode?> GetCollectionModeAsync(
        Guid collectionId,
        CancellationToken cancellationToken) {
        var row = await db.Entities
            .Where(entity =>
                entity.Id == collectionId &&
                entity.KindCode == EntityKindRegistry.Collection.Code &&
                entity.DeletedAt == null)
            .Join(
                db.CollectionDetails,
                entity => entity.Id,
                detail => detail.EntityId,
                (_, detail) => new { detail.Mode })
            .FirstOrDefaultAsync(cancellationToken);
        return row?.Mode;
    }

    private async Task TouchCollectionAsync(
        Guid collectionId,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var entity = await db.Entities.FindAsync([collectionId], cancellationToken);
        if (entity is not null) {
            entity.UpdatedAt = now;
        }
    }

    private async Task<int> CountItemsAsync(Guid collectionId, CancellationToken cancellationToken) =>
        await db.CollectionItemDetails.CountAsync(row => row.CollectionEntityId == collectionId, cancellationToken);

    private async Task<int> NextSortOrderAsync(Guid collectionId, CancellationToken cancellationToken) =>
        (await db.CollectionItemDetails
            .Where(row => row.CollectionEntityId == collectionId)
            .Select(row => (int?)row.SortOrder)
            .MaxAsync(cancellationToken) ?? -1) + 1;

    private CollectionDetailRow TrackCollectionDetail(Guid collectionId) {
        var detail = new CollectionDetailRow { EntityId = collectionId };
        db.CollectionDetails.Add(detail);
        return detail;
    }

    private async Task<IReadOnlyList<VisibleRuleMatch>> VisibleMatchesAsync(
        IReadOnlyList<CollectionRuleMatch> matches,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var ids = matches.Select(match => match.EntityId).Distinct().ToArray();
        if (ids.Length == 0) {
            return [];
        }

        var query = db.Entities.AsNoTracking()
            .Where(row => ids.Contains(row.Id) && row.DeletedAt == null);
        if (hideNsfw) {
            query = query.Where(row => !row.IsNsfw);
        }

        var rows = await query.ToDictionaryAsync(row => row.Id, cancellationToken);
        var seen = new HashSet<Guid>();
        return matches
            .Where(match => seen.Add(match.EntityId))
            .Select(match => rows.TryGetValue(match.EntityId, out var row)
                ? new VisibleRuleMatch(row.KindCode, row.Id)
                : null)
            .Where(match => match is not null)
            .Select(match => match!)
            .ToArray();
    }

    private static IReadOnlyList<CollectionItemReference> DistinctReferences(
        IReadOnlyList<CollectionItemReference> references) {
        var seen = new HashSet<Guid>();
        return references
            .Where(reference => reference.EntityId != Guid.Empty)
            .Where(reference => seen.Add(reference.EntityId))
            .Select(reference => reference with { EntityType = reference.EntityType.Trim() })
            .ToArray();
    }

    private sealed record NormalizedCollectionWrite(
        string Title,
        string? Description,
        CollectionMode Mode,
        string? RuleTreeJson,
        CollectionCoverMode CoverMode,
        Guid? CoverItemId,
        bool IsNsfw);

    private sealed record VisibleRuleMatch(string EntityType, Guid EntityId);
}
