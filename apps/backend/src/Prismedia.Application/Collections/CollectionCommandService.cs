using System.Globalization;
using System.Text.Json;
using Prismedia.Application.Entities;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Security;
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
    ICollectionRefreshPersistence refreshPersistence,
    ICurrentUserContext currentUser) : ICollectionCommandService {
    private const int PreviewSampleSize = 24;
    private static readonly string EmptyRuleJson =
        $$"""{"type":"group","operator":"{{CollectionRuleGroupOperator.And.ToCode()}}","children":[]}""";

    /// <inheritdoc />
    public async Task<CollectionWriteResult> CreateAsync(
        CollectionWriteRequest request,
        CancellationToken cancellationToken) {
        if (!TryNormalizeWrite(request, out var write, out var message)) {
            return InvalidWrite(message);
        }

        var collection = CreateCollection(Guid.NewGuid(), write, write.IsShared ?? false);
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

        var current = await persistence.GetEditStateAsync(
            collectionId,
            currentUser.UserId,
            cancellationToken);
        if (current is null) {
            return new CollectionWriteResult(CollectionCommandStatus.NotFound, Message: "Collection was not found.");
        }

        var collection = CreateCollection(collectionId, write, write.IsShared ?? current.IsShared);
        if (!await persistence.UpdateAsync(collection, write.Description, currentUser.UserId, cancellationToken)) {
            return new CollectionWriteResult(CollectionCommandStatus.NotFound, Message: "Collection was not found.");
        }

        await RefreshRulesAfterWriteAsync(collectionId, collection, cancellationToken);
        return await DetailResultAsync(collectionId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CollectionCommandResult> DeleteAsync(
        Guid collectionId,
        CancellationToken cancellationToken) =>
        await persistence.DeleteAsync(collectionId, currentUser.UserId, cancellationToken)
            ? new CollectionCommandResult(CollectionCommandStatus.Succeeded)
            : new CollectionCommandResult(CollectionCommandStatus.NotFound, "Collection was not found.");

    /// <inheritdoc />
    public async Task<CollectionCountResult> AddItemsAsync(
        Guid collectionId,
        CollectionAddItemsRequest request,
        CancellationToken cancellationToken) {
        var membership = await ManualMembershipAsync(
            collectionId,
            allowSharedContribution: true,
            "Dynamic collections are populated by rules.",
            cancellationToken);
        if (membership is not null) {
            return membership;
        }

        var references = DistinctReferences(request.Items ?? []);
        if (references.Count == 0) {
            return new CollectionCountResult(CollectionCommandStatus.Invalid, Message: "At least one item is required.");
        }

        var requested = new List<(CollectionItemReference Reference, EntityKind Kind)>();
        foreach (var reference in references) {
            var kind = reference.EntityType;
            if (!Collection.CanContain(kind)) {
                return new CollectionCountResult(
                    CollectionCommandStatus.Invalid,
                    Message: $"Entity type '{EntityKindRegistry.ToCode(kind)}' cannot be added to a collection.");
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
            currentUser.UserId,
            requested.Select(item => item.Reference.EntityId).ToArray(),
            cancellationToken);
        return new CollectionCountResult(CollectionCommandStatus.Succeeded, added);
    }

    /// <inheritdoc />
    public async Task<CollectionCountResult> RemoveItemsAsync(
        Guid collectionId,
        CollectionRemoveItemsRequest request,
        CancellationToken cancellationToken) {
        var membership = await ManualMembershipAsync(
            collectionId,
            allowSharedContribution: false,
            "Dynamic collections are changed by refreshing their rules.",
            cancellationToken);
        if (membership is not null) {
            return membership;
        }

        var itemIds = (request.ItemIds ?? []).Distinct().ToArray();
        var removed = await persistence.RemoveItemsAsync(
            collectionId,
            currentUser.UserId,
            itemIds,
            cancellationToken);
        return new CollectionCountResult(CollectionCommandStatus.Succeeded, removed);
    }

    /// <inheritdoc />
    public async Task<CollectionCountResult> ReorderItemsAsync(
        Guid collectionId,
        CollectionReorderItemsRequest request,
        CancellationToken cancellationToken) {
        var membership = await ManualMembershipAsync(
            collectionId,
            allowSharedContribution: false,
            "Dynamic collections are changed by refreshing their rules.",
            cancellationToken);
        if (membership is not null) {
            return membership;
        }

        var reordered = await persistence.ReorderItemsAsync(
            collectionId,
            currentUser.UserId,
            (request.ItemIds ?? []).Distinct().ToArray(),
            cancellationToken);
        return new CollectionCountResult(CollectionCommandStatus.Succeeded, reordered);
    }

    /// <inheritdoc />
    public async Task<CollectionRulePreviewResponse?> PreviewRulesAsync(
        CollectionRulePreviewRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(request.RuleTreeJson) || !ValidateRuleTree(request.RuleTreeJson, out _)) {
            return null;
        }

        var matches = await ruleEngine.EvaluateAsync(
            request.RuleTreeJson,
            currentUser.UserId,
            cancellationToken);
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
                ? new CollectionRulePreviewItem(match.EntityType.DecodeAs<EntityKind>(), match.EntityId, thumbnail)
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
        if (!await persistence.ExistsAsync(collectionId, currentUser.UserId, cancellationToken)) {
            return null;
        }

        var collection = await refreshPersistence.GetDynamicCollectionAsync(collectionId, cancellationToken);
        if (collection is null) {
            var currentCount = await persistence.CountItemsAsync(
                collectionId,
                currentUser.UserId,
                cancellationToken);
            return new CollectionRefreshResponse(false, currentCount);
        }

        var matches = await ruleEngine.EvaluateAsync(
            collection.RuleTreeJson,
            collection.OwnerUserId,
            cancellationToken);
        await refreshPersistence.RefreshCollectionItemsAsync(collectionId, matches, cancellationToken);
        return new CollectionRefreshResponse(
            true,
            await persistence.CountItemsAsync(collectionId, currentUser.UserId, cancellationToken));
    }

    private async Task<CollectionCountResult?> ManualMembershipAsync(
        Guid collectionId,
        bool allowSharedContribution,
        string invalidMessage,
        CancellationToken cancellationToken) {
        var state = await persistence.GetMembershipStateAsync(
            collectionId,
            cancellationToken);
        if (state is null) {
            return new CollectionCountResult(CollectionCommandStatus.NotFound, Message: "Collection was not found.");
        }

        var collection = new Collection(
            collectionId,
            "Collection",
            state.OwnerUserId,
            state.Mode,
            state.Mode == CollectionMode.Manual ? null : EmptyRuleJson,
            isShared: state.IsShared);
        var mayChangeMembership = collection.IsOwnedBy(currentUser.UserId) ||
            (allowSharedContribution && collection.CanContributeItems(currentUser.UserId));
        if (!mayChangeMembership) {
            return new CollectionCountResult(CollectionCommandStatus.NotFound, Message: "Collection was not found.");
        }

        return collection.CanEditManualMembership
            ? null
            : new CollectionCountResult(CollectionCommandStatus.Invalid, Message: invalidMessage);
    }

    private Collection CreateCollection(Guid id, NormalizedCollectionWrite write, bool isShared) {
        var collection = new Collection(
            id,
            write.Title,
            currentUser.UserId,
            write.Mode,
            write.RuleTreeJson,
            write.CoverMode,
            write.CoverItemId,
            isShared: isShared);
        collection.PatchFlags(isFavorite: null, isNsfw: write.IsNsfw, isOrganized: null);
        return collection;
    }

    private async Task<CollectionWriteResult> DetailResultAsync(
        Guid collectionId,
        CancellationToken cancellationToken) {
        var collection = await entities.GetAsync(
            collectionId,
            EntityKind.Collection.ToCode(),
            hideNsfw: false,
            cancellationToken);
        return collection is not null
            ? new CollectionWriteResult(CollectionCommandStatus.Succeeded, collection)
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

        var matches = await ruleEngine.EvaluateAsync(
            collection.RuleTreeJson,
            collection.OwnerUserId,
            cancellationToken);
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

        // Mode/CoverMode are decoded at the deserialization boundary now (the wire still carries
        // the string code); an unknown value fails request binding rather than reaching here.
        var mode = request.Mode ?? CollectionMode.Manual;
        var coverMode = request.CoverMode ?? CollectionCoverMode.Mosaic;

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
            request.IsNsfw ?? false,
            request.IsShared);
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
            if (node is CollectionRuleGroup group && ValidateRuleNode(group, out message)) {
                return true;
            }

            if (string.IsNullOrWhiteSpace(message)) {
                message = "Collection rule tree must be a group node.";
            }
            return false;
        } catch (JsonException) {
            message = "Collection rule tree is not valid JSON.";
            return false;
        } catch (NotSupportedException) {
            message = "Collection rule tree contains an unsupported node.";
            return false;
        }
    }

    private static bool ValidateRuleNode(CollectionRuleNode node, out string message) {
        message = string.Empty;
        return node switch {
            CollectionRuleGroup group => ValidateRuleGroup(group, out message),
            CollectionRuleCondition condition => ValidateRuleCondition(condition, out message),
            _ => InvalidRule("Collection rule tree contains an unsupported node.", out message)
        };
    }

    private static bool ValidateRuleGroup(CollectionRuleGroup group, out string message) {
        message = string.Empty;
        if (!group.Operator.TryDecodeAs<CollectionRuleGroupOperator>(out _)) {
            message = "Collection rule group has an unsupported operator.";
            return false;
        }

        if (group.Children is null) {
            message = "Collection rule group children must be an array.";
            return false;
        }

        foreach (var child in group.Children) {
            if (!ValidateRuleNode(child, out message)) {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateRuleCondition(CollectionRuleCondition condition, out string message) {
        message = string.Empty;
        if (condition.EntityTypes is null) {
            message = "Collection rule condition entity types must be an array.";
            return false;
        }

        foreach (var entityType in condition.EntityTypes) {
            if (string.IsNullOrWhiteSpace(entityType) ||
                !EntityKindRegistry.TryGet(entityType, out var kind) ||
                !Collection.CanContain(kind)) {
                message = $"Collection rule condition entity type '{entityType}' is not supported.";
                return false;
            }
        }

        if (!condition.Field.TryDecodeAs<CollectionRuleField>(out var field)) {
            message = $"Collection rule condition field '{condition.Field}' is not supported.";
            return false;
        }

        if (!condition.Operator.TryDecodeAs<CollectionRuleOperator>(out var op)) {
            message = $"Collection rule condition operator '{condition.Operator}' is not supported.";
            return false;
        }

        if (op is CollectionRuleOperator.IsNull or CollectionRuleOperator.IsNotNull or
            CollectionRuleOperator.IsTrue or CollectionRuleOperator.IsFalse) {
            return true;
        }

        if (!condition.Value.HasValue ||
            condition.Value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) {
            message = "Collection rule condition value is required.";
            return false;
        }

        var value = condition.Value.Value;
        if (op is CollectionRuleOperator.Between) {
            return ValidateBetweenValue(field, value, out message);
        }

        if (op is CollectionRuleOperator.In or CollectionRuleOperator.NotIn) {
            return ValidateArrayValue(field, value, out message);
        }

        if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) {
            message = "Collection rule condition value must be a scalar.";
            return false;
        }

        return ValidateTypedScalarValue(field, value, out message);
    }

    private static bool ValidateBetweenValue(CollectionRuleField field, JsonElement value, out string message) {
        message = string.Empty;
        if (value.ValueKind != JsonValueKind.Array) {
            message = "Collection rule between value must be an array.";
            return false;
        }

        var count = 0;
        foreach (var item in value.EnumerateArray()) {
            if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null or JsonValueKind.Undefined) {
                message = "Collection rule between values must be scalar.";
                return false;
            }
            count++;
        }

        if (count != 2) {
            message = "Collection rule between value must contain exactly two values.";
            return false;
        }

        return field switch {
            CollectionRuleField.Date => value.EnumerateArray().All(IsDateValue) ||
                InvalidRule("Collection rule date values must be valid dates.", out message),
            CollectionRuleField.CreatedAt => value.EnumerateArray().All(IsDateTimeValue) ||
                InvalidRule("Collection rule added-date values must be valid timestamps.", out message),
            _ => true
        };
    }

    private static bool ValidateArrayValue(CollectionRuleField field, JsonElement value, out string message) {
        message = string.Empty;
        if (value.ValueKind != JsonValueKind.Array) {
            message = "Collection rule list value must be an array.";
            return false;
        }

        foreach (var item in value.EnumerateArray()) {
            if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null or JsonValueKind.Undefined) {
                message = "Collection rule list values must be scalar.";
                return false;
            }
        }

        if (field == CollectionRuleField.LibraryRootId && !value.EnumerateArray().All(IsGuidValue)) {
            message = "Collection rule library values must be valid IDs.";
            return false;
        }

        return true;
    }

    private static bool ValidateTypedScalarValue(CollectionRuleField field, JsonElement value, out string message) {
        message = string.Empty;
        return field switch {
            CollectionRuleField.Date when !IsDateValue(value) =>
                InvalidRule("Collection rule date value must be a valid date.", out message),
            CollectionRuleField.CreatedAt when !IsDateTimeValue(value) =>
                InvalidRule("Collection rule added-date value must be a valid timestamp.", out message),
            CollectionRuleField.LibraryRootId when !IsGuidValue(value) =>
                InvalidRule("Collection rule library value must be a valid ID.", out message),
            _ => true
        };
    }

    private static bool IsDateValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsDateTimeValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsGuidValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        Guid.TryParse(value.GetString(), out _);

    private static bool InvalidRule(string invalidMessage, out string message) {
        message = invalidMessage;
        return false;
    }

    private static IReadOnlyList<CollectionItemReference> DistinctReferences(
        IReadOnlyList<CollectionItemReference> references) {
        var seen = new HashSet<Guid>();
        return references
            .Where(reference => reference.EntityId != Guid.Empty)
            .Where(reference => seen.Add(reference.EntityId))
            .ToArray();
    }

    private sealed record NormalizedCollectionWrite(
        string Title,
        string? Description,
        CollectionMode Mode,
        string? RuleTreeJson,
        CollectionCoverMode CoverMode,
        Guid? CoverItemId,
        bool IsNsfw,
        bool? IsShared);
}
