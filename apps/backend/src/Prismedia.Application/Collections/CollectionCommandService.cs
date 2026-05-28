using System.Text.Json;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Collections;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;

namespace Prismedia.Application.Collections;

/// <summary>
/// Application service for collection command use cases. It owns request validation,
/// collection-domain decisions, rule evaluation, and persistence side-effect ordering.
/// </summary>
public sealed class CollectionCommandService(
    ICollectionCommandPersistence persistence,
    IEntityReadService entities,
    ICollectionRuleEngine ruleEngine,
    ICollectionRefreshPersistence refreshPersistence) : ICollectionCommandService {
    private const int PreviewSampleSize = 24;
    private const string EmptyRuleJson = """{"type":"group","operator":"and","children":[]}""";

    /// <inheritdoc />
    public async Task<CollectionWriteResult> CreateAsync(
        CollectionWriteRequest request,
        CancellationToken cancellationToken) {
        if (!TryNormalizeWrite(request, out var write, out var message)) {
            return InvalidWrite(message);
        }

        var collection = CreateCollection(Guid.NewGuid(), write);
        var collectionId = await persistence.CreateAsync(collection, write.Description, cancellationToken);
        await RefreshRulesAfterWriteAsync(collectionId, collection, cancellationToken);
        return await DetailResultAsync(collectionId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CollectionWriteResult> UpdateAsync(
        Guid collectionId,
        CollectionWriteRequest request,
        CancellationToken cancellationToken) {
        if (!TryNormalizeWrite(request, out var write, out var message)) {
            return InvalidWrite(message);
        }

        var collection = CreateCollection(collectionId, write);
        if (!await persistence.UpdateAsync(collection, write.Description, cancellationToken)) {
            return new CollectionWriteResult(CollectionCommandStatus.NotFound, Message: "Collection was not found.");
        }

        await RefreshRulesAfterWriteAsync(collectionId, collection, cancellationToken);
        return await DetailResultAsync(collectionId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CollectionCommandResult> DeleteAsync(
        Guid collectionId,
        CancellationToken cancellationToken) =>
        await persistence.DeleteAsync(collectionId, cancellationToken)
            ? new CollectionCommandResult(CollectionCommandStatus.Succeeded)
            : new CollectionCommandResult(CollectionCommandStatus.NotFound, "Collection was not found.");

    /// <inheritdoc />
    public async Task<CollectionCountResult> AddItemsAsync(
        Guid collectionId,
        CollectionAddItemsRequest request,
        CancellationToken cancellationToken) {
        var membership = await ManualMembershipAsync(collectionId, "Dynamic collections are populated by rules.", cancellationToken);
        if (membership is not null) {
            return membership;
        }

        var references = DistinctReferences(request.Items ?? []);
        if (references.Count == 0) {
            return new CollectionCountResult(CollectionCommandStatus.Invalid, Message: "At least one item is required.");
        }

        var requested = new List<(CollectionItemReference Reference, EntityKind Kind)>();
        foreach (var reference in references) {
            if (!EntityKindRegistry.TryGet(reference.EntityType, out var kind) || !Collection.CanContain(kind)) {
                return new CollectionCountResult(
                    CollectionCommandStatus.Invalid,
                    Message: $"Entity type '{reference.EntityType}' cannot be added to a collection.");
            }

            requested.Add((reference, kind));
        }

        var activeItems = await persistence.GetActiveItemsAsync(
            requested.Select(item => item.Reference.EntityId).ToArray(), cancellationToken);
        foreach (var item in requested) {
            if (!activeItems.TryGetValue(item.Reference.EntityId, out var candidate) ||
                candidate.EntityKind != item.Kind) {
                return new CollectionCountResult(
                    CollectionCommandStatus.Invalid,
                    Message: "One or more collection items were not found or had the wrong entity type.");
            }
        }

        var added = await persistence.AddManualItemsAsync(
            collectionId,
            requested.Select(item => item.Reference.EntityId).ToArray(),
            cancellationToken);
        return new CollectionCountResult(CollectionCommandStatus.Succeeded, added);
    }

    /// <inheritdoc />
    public async Task<CollectionCountResult> RemoveItemsAsync(
        Guid collectionId,
        CollectionRemoveItemsRequest request,
        CancellationToken cancellationToken) {
        var membership = await ManualMembershipAsync(collectionId, "Dynamic collections are changed by refreshing their rules.", cancellationToken);
        if (membership is not null) {
            return membership;
        }

        var itemIds = (request.ItemIds ?? []).Distinct().ToArray();
        var removed = await persistence.RemoveItemsAsync(collectionId, itemIds, cancellationToken);
        return new CollectionCountResult(CollectionCommandStatus.Succeeded, removed);
    }

    /// <inheritdoc />
    public async Task<CollectionCountResult> ReorderItemsAsync(
        Guid collectionId,
        CollectionReorderItemsRequest request,
        CancellationToken cancellationToken) {
        var membership = await ManualMembershipAsync(collectionId, "Dynamic collections are changed by refreshing their rules.", cancellationToken);
        if (membership is not null) {
            return membership;
        }

        var reordered = await persistence.ReorderItemsAsync(
            collectionId,
            (request.ItemIds ?? []).Distinct().ToArray(),
            cancellationToken);
        return new CollectionCountResult(CollectionCommandStatus.Succeeded, reordered);
    }

    /// <inheritdoc />
    public async Task<CollectionRulePreviewResponse?> PreviewRulesAsync(
        CollectionRulePreviewRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (!ValidateRuleTree(request.RuleTreeJson, out _)) {
            return null;
        }

        var matches = await ruleEngine.EvaluateAsync(request.RuleTreeJson, cancellationToken);
        var visible = await persistence.FilterVisibleRuleMatchesAsync(matches, hideNsfw, cancellationToken);
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

    /// <inheritdoc />
    public async Task<CollectionRefreshResponse?> RefreshAsync(
        Guid collectionId,
        CancellationToken cancellationToken) {
        if (!await persistence.ExistsAsync(collectionId, cancellationToken)) {
            return null;
        }

        var collection = await refreshPersistence.GetDynamicCollectionAsync(collectionId, cancellationToken);
        if (collection is null) {
            var currentCount = await persistence.CountItemsAsync(collectionId, cancellationToken);
            return new CollectionRefreshResponse(false, currentCount);
        }

        var matches = await ruleEngine.EvaluateAsync(collection.RuleTreeJson, cancellationToken);
        await refreshPersistence.RefreshCollectionItemsAsync(collectionId, matches, cancellationToken);
        return new CollectionRefreshResponse(true, await persistence.CountItemsAsync(collectionId, cancellationToken));
    }

    private async Task<CollectionCountResult?> ManualMembershipAsync(
        Guid collectionId,
        string invalidMessage,
        CancellationToken cancellationToken) {
        var mode = await persistence.GetModeAsync(collectionId, cancellationToken);
        if (mode is null) {
            return new CollectionCountResult(CollectionCommandStatus.NotFound, Message: "Collection was not found.");
        }

        var collection = new Collection(
            collectionId,
            "Collection",
            mode.Value,
            mode == CollectionMode.Manual ? null : EmptyRuleJson);
        return collection.CanEditManualMembership
            ? null
            : new CollectionCountResult(CollectionCommandStatus.Invalid, Message: invalidMessage);
    }

    private static Collection CreateCollection(Guid id, NormalizedCollectionWrite write) {
        var collection = new Collection(id, write.Title, write.Mode, write.RuleTreeJson, write.CoverMode, write.CoverItemId);
        collection.PatchFlags(isFavorite: null, isNsfw: write.IsNsfw, isOrganized: null);
        return collection;
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
        Collection collection,
        CancellationToken cancellationToken) {
        if (!collection.UsesRules || string.IsNullOrWhiteSpace(collection.RuleTreeJson)) {
            return;
        }

        var matches = await ruleEngine.EvaluateAsync(collection.RuleTreeJson, cancellationToken);
        await refreshPersistence.RefreshCollectionItemsAsync(collectionId, matches, cancellationToken);
    }

    private static bool TryNormalizeWrite(
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
}
