using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Filesystem settings for artwork downloaded while applying plugin metadata.
/// </summary>
/// <param name="CacheRoot">Physical cache root served by the API under /assets.</param>
public sealed record PluginArtworkServiceOptions(string CacheRoot);

/// <summary>
/// Applies selected plugin metadata proposals into entity capability rows.
/// </summary>
public sealed class EntityMetadataApplyService : IEntityMetadataPatchService {
    private static readonly HashSet<string> IgnoredStatCodes = new(StringComparer.OrdinalIgnoreCase) {
        "popularity"
    };

    private readonly PrismediaDbContext _db;
    private readonly PluginArtworkServiceOptions _options;
    private readonly HttpClient _http;

    /// <summary>
    /// Creates an apply service over EF Core rows and optional artwork downloading.
    /// </summary>
    /// <param name="db">Database context that owns entity capability tables.</param>
    /// <param name="options">Filesystem settings for downloaded artwork.</param>
    /// <param name="http">Optional HTTP client for tests or configured hosts.</param>
    public EntityMetadataApplyService(
        PrismediaDbContext db,
        PluginArtworkServiceOptions options,
        HttpClient? http = null) {
        _db = db;
        _options = options;
        _http = http ?? new HttpClient();
    }

    /// <summary>
    /// Applies a user-authored metadata patch to one entity. Only explicitly scoped fields
    /// are mutated, allowing callers to replace or clear individual editable sections without
    /// sending the entire entity shape.
    /// </summary>
    /// <param name="entityId">Entity receiving the patch.</param>
    /// <param name="request">Scoped metadata update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the entity exists and was updated; false when no active entity exists.</returns>
    public async Task<bool> ApplyPatchAsync(
        Guid entityId,
        EntityMetadataUpdateRequest request,
        CancellationToken cancellationToken) =>
        await ApplyPatchAsync(entityId, request, expectedKind: null, cancellationToken) == EntityMetadataPatchResult.Applied;

    /// <inheritdoc />
    public async Task<EntityMetadataPatchResult> ApplyPatchAsync(
        Guid entityId,
        EntityMetadataUpdateRequest request,
        string? expectedKind,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Patch);

        var fields = EntityMetadataPatchValidator.NormalizeFieldSet(request.Fields);
        EntityMetadataPatchValidator.Validate(fields, request.Patch);

        var entity = await _db.Entities
            .FirstOrDefaultAsync(row => row.Id == entityId && row.DeletedAt == null, cancellationToken);
        if (entity is null) {
            return EntityMetadataPatchResult.NotFound;
        }

        if (!string.IsNullOrWhiteSpace(expectedKind) &&
            !IsKindCompatible(entity.KindCode, expectedKind)) {
            return EntityMetadataPatchResult.KindMismatch;
        }

        var now = DateTimeOffset.UtcNow;
        await ApplyScopedPatchToEntityAsync(entity, fields, request.Patch, now, cancellationToken);

        if (fields.Contains("images") && request.SelectedImages is not null) {
            await DownloadSelectedImagesAsync(entityId, request.SelectedImages, now, cancellationToken);
        }

        if (request.Children is { Count: > 0 }) {
            await ApplyStructuralChildrenAsync(request.Children, now, [entity.Id], cancellationToken);
        }

        if (request.Relationships is { Count: > 0 } &&
            (fields.Contains("credits") || fields.Contains("studio") || fields.Contains("tags"))) {
            await ApplyRelationshipProposalsAsync(entityId, request.Relationships, now, cancellationToken);
        }

        entity.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return EntityMetadataPatchResult.Applied;
    }

    private async Task ApplyScopedPatchToEntityAsync(
        EntityRow entity,
        ISet<string> fields,
        EntityMetadataPatch patch,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (fields.Contains("title")) {
            entity.Title = patch.Title!.Trim();
        }

        if (fields.Contains("description")) {
            await UpsertDescriptionAsync(entity.Id, patch.Description, now, cancellationToken);
        }

        if (fields.Contains("externalIds")) {
            await ReplaceExternalIdsAsync(entity.Id, patch.ExternalIds, patch.Urls, now, cancellationToken);
        }

        if (fields.Contains("urls")) {
            await ReplaceUrlsAsync(entity.Id, patch.Urls, now, cancellationToken);
        }

        if (fields.Contains("tags")) {
            await ReplaceTagsAsync(entity.Id, patch.Tags, now, cancellationToken);
        }

        if (fields.Contains("studio")) {
            await RemoveRelationshipAsync(entity.Id, "studio", cancellationToken);
            if (!string.IsNullOrWhiteSpace(patch.Studio)) {
                await SetStudioAsync(entity.Id, patch.Studio, now, cancellationToken);
            }
        }

        if (fields.Contains("credits")) {
            await ReplaceCreditsAsync(entity.Id, patch.Credits, now, cancellationToken);
        }

        if (fields.Contains("dates")) {
            await ReplaceDatesAsync(entity.Id, patch.Dates, now, cancellationToken);
        }

        if (fields.Contains("stats")) {
            await ReplaceStatsAsync(entity.Id, patch.Stats, now, cancellationToken);
        }

        if (fields.Contains("positions")) {
            await ReplacePositionsAsync(entity, NormalizePositions(patch.Positions), now, cancellationToken);
        }

        if (fields.Contains("classification")) {
            await ReplaceClassificationAsync(entity.Id, patch.Classification, now, cancellationToken);
        }

        if (fields.Contains("rating")) {
            await UpsertRatingAsync(entity.Id, patch.Rating, now, cancellationToken);
        }

        if (fields.Contains("flags")) {
            await UpsertFlagsAsync(entity.Id, patch.Flags, now, cancellationToken);
        }
    }

    /// <summary>
    /// Applies selected fields from a proposal to an existing entity.
    /// </summary>
    /// <param name="entityId">Entity receiving metadata.</param>
    /// <param name="proposal">Plugin proposal chosen by the user.</param>
    /// <param name="selectedFields">Field keys selected in the review UI.</param>
    /// <param name="selectedImages">Optional role-to-remote-URL artwork selections.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the entity exists and was updated.</returns>
    public async Task<bool> ApplyAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        IReadOnlyCollection<string> selectedFields,
        IReadOnlyDictionary<string, string?>? selectedImages,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(selectedFields);

        var entity = await _db.Entities
            .FirstOrDefaultAsync(row => row.Id == entityId && row.DeletedAt == null, cancellationToken);
        if (entity is null) {
            return false;
        }

        var selected = selectedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var patch = proposal.Patch;
        var now = DateTimeOffset.UtcNow;

        if (selected.Contains("title") && !string.IsNullOrWhiteSpace(patch.Title)) {
            entity.Title = patch.Title.Trim();
        }

        if (selected.Contains("description")) {
            await UpsertDescriptionAsync(entityId, patch.Description, now, cancellationToken);
        }

        if (selected.Contains("externalIds")) {
            await UpsertExternalIdsAsync(entityId, patch.ExternalIds, patch.Urls, now, cancellationToken);
        }

        if (selected.Contains("urls")) {
            await UpsertUrlsAsync(entityId, patch.Urls, now, cancellationToken);
        }

        if (selected.Contains("tags")) {
            await ReplaceTagsAsync(entityId, patch.Tags, now, cancellationToken);
        }

        if (selected.Contains("studio") && !string.IsNullOrWhiteSpace(patch.Studio)) {
            await SetStudioAsync(entityId, patch.Studio, now, cancellationToken);
        }

        if (selected.Contains("credits")) {
            await ReplaceCreditsAsync(entityId, patch.Credits, now, cancellationToken);
        }

        if (selected.Contains("dates")) {
            await UpsertDatesAsync(entityId, patch.Dates, now, cancellationToken);
        }

        if (selected.Contains("stats")) {
            await UpsertStatsAsync(entityId, patch.Stats, now, cancellationToken);
        }

        if (selected.Contains("positions")) {
            var normalizedPositions = NormalizePositions(patch.Positions);
            await UpsertPositionsAsync(entity, normalizedPositions, now, cancellationToken);
        }

        if (selected.Contains("classification")) {
            await UpsertClassificationAsync(entityId, patch.Classification, now, cancellationToken);
        }

        if (selected.Contains("images") && selectedImages is not null) {
            await DownloadSelectedImagesAsync(entityId, selectedImages, now, cancellationToken);
        }

        if (patch.Flags?.IsNsfw == true) {
            await UpsertFlagsAsync(entityId, new EntityMetadataFlagsPatch(null, true, null), now, cancellationToken);
        }

        var relationshipProposals = RelationshipProposals(proposal);
        if (relationshipProposals.Count > 0 && (selected.Contains("credits") || selected.Contains("studio") || selected.Contains("tags"))) {
            await ApplyRelationshipProposalsAsync(entityId, relationshipProposals, now, cancellationToken);
        }

        await ApplyStructuralChildrenAsync(StructuralChildProposals(proposal), now, [entity.Id], cancellationToken);

        entity.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task UpsertDescriptionAsync(Guid entityId, string? value, DateTimeOffset now, CancellationToken cancellationToken) {
        var existing = await _db.EntityDescriptions.FindAsync([entityId], cancellationToken);
        if (string.IsNullOrWhiteSpace(value)) {
            if (existing is not null) {
                _db.EntityDescriptions.Remove(existing);
            }
            return;
        }

        if (existing is null) {
            _db.EntityDescriptions.Add(new EntityDescriptionRow { EntityId = entityId, Value = value.Trim(), UpdatedAt = now });
        } else {
            existing.Value = value.Trim();
            existing.UpdatedAt = now;
        }
    }

    private async Task ReplaceUrlsAsync(Guid entityId, IReadOnlyList<string> urls, DateTimeOffset now, CancellationToken cancellationToken) {
        var existing = await _db.EntityUrls
            .Where(row => row.EntityId == entityId)
            .ToArrayAsync(cancellationToken);
        _db.EntityUrls.RemoveRange(existing);

        var order = 0;
        foreach (var url in urls.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)) {
            _db.EntityUrls.Add(new EntityUrlRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Url = url,
                SortOrder = order++,
                CreatedAt = now
            });
        }
    }

    private async Task ReplaceExternalIdsAsync(
        Guid entityId,
        IReadOnlyDictionary<string, string> externalIds,
        IReadOnlyList<string> urls,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var existing = await _db.EntityExternalIds
            .Where(row => row.EntityId == entityId)
            .ToArrayAsync(cancellationToken);
        _db.EntityExternalIds.RemoveRange(existing);

        foreach (var (provider, rawValue) in externalIds) {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(rawValue)) {
                continue;
            }

            var value = rawValue.Trim();
            var url = urls.FirstOrDefault(candidate => candidate.Contains(value, StringComparison.OrdinalIgnoreCase));
            _db.EntityExternalIds.Add(new EntityExternalIdRow {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                Provider = provider.Trim(),
                Value = value,
                Url = url,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    private async Task UpsertExternalIdsAsync(
        Guid entityId,
        IReadOnlyDictionary<string, string> externalIds,
        IReadOnlyList<string> urls,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        foreach (var (provider, rawValue) in externalIds) {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(rawValue)) {
                continue;
            }

            var providerKey = provider.Trim();
            var value = rawValue.Trim();
            var existing = _db.EntityExternalIds.Local.FirstOrDefault(row =>
                row.EntityId == entityId &&
                row.Provider == providerKey &&
                _db.Entry(row).State != EntityState.Deleted) ??
                await _db.EntityExternalIds
                    .FirstOrDefaultAsync(row => row.EntityId == entityId && row.Provider == providerKey, cancellationToken);
            var url = urls.FirstOrDefault(candidate => candidate.Contains(value, StringComparison.OrdinalIgnoreCase));
            if (existing is null) {
                _db.EntityExternalIds.Add(new EntityExternalIdRow {
                    Id = Guid.NewGuid(),
                    EntityId = entityId,
                    Provider = providerKey,
                    Value = value,
                    Url = url,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            } else {
                existing.Value = value;
                existing.Url = url ?? existing.Url;
                existing.UpdatedAt = now;
            }
        }
    }

    private async Task UpsertUrlsAsync(Guid entityId, IReadOnlyList<string> urls, DateTimeOffset now, CancellationToken cancellationToken) {
        var existing = await _db.EntityUrls
            .Where(row => row.EntityId == entityId)
            .Select(row => row.Url)
            .ToArrayAsync(cancellationToken);
        var tracked = _db.EntityUrls.Local
            .Where(row => row.EntityId == entityId && _db.Entry(row).State != EntityState.Deleted)
            .Select(row => row.Url);
        var seen = existing.Concat(tracked).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sortOrder = existing.Length;

        foreach (var url in urls.Where(url => !string.IsNullOrWhiteSpace(url)).Select(url => url.Trim())) {
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

    private async Task ReplaceTagsAsync(Guid entityId, IReadOnlyList<string> tags, DateTimeOffset now, CancellationToken cancellationToken) {
        await RemoveRelationshipAsync(entityId, "tags", cancellationToken);

        var order = 0;
        foreach (var name in tags.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)) {
            var tag = await FindEntityByKindAndTitleAsync("tag", name, cancellationToken)
                ?? CreateEntity("tag", name, now);
            AddRelationship(entityId, "tags", "Tags", tag.Id, tag.KindCode, order++, null, now);
        }
    }

    private async Task SetStudioAsync(Guid entityId, string studioName, DateTimeOffset now, CancellationToken cancellationToken) {
        var studio = await FindEntityByKindAndTitleAsync("studio", studioName.Trim(), cancellationToken)
            ?? CreateEntity("studio", studioName.Trim(), now);
        await RemoveRelationshipAsync(entityId, "studio", cancellationToken);
        AddRelationship(entityId, "studio", "Studio", studio.Id, studio.KindCode, 0, null, now);
    }

    private async Task ReplaceCreditsAsync(Guid entityId, IReadOnlyList<CreditPatch> credits, DateTimeOffset now, CancellationToken cancellationToken) {
        await RemoveRelationshipAsync(entityId, "cast", cancellationToken);

        var order = 0;
        var resolvedPeople = new Dictionary<string, EntityRow>(StringComparer.OrdinalIgnoreCase);
        var linkedCredits = new Dictionary<Guid, CreditRelationshipAccumulator>();
        foreach (var credit in credits.Where(credit => !string.IsNullOrWhiteSpace(credit.Name))) {
            var personName = credit.Name.Trim();
            if (!resolvedPeople.TryGetValue(personName, out var person)) {
                person = await FindEntityByKindAndTitleAsync("person", personName, cancellationToken)
                    ?? CreateEntity("person", personName, now);
                resolvedPeople[personName] = person;
            }

            var role = string.IsNullOrWhiteSpace(credit.Role) ? "person" : credit.Role.Trim();
            var character = string.IsNullOrWhiteSpace(credit.Character) ? null : credit.Character.Trim();
            var fallbackSortOrder = order++;
            var sortOrder = credit.SortOrder ?? fallbackSortOrder;
            if (!linkedCredits.TryGetValue(person.Id, out var accumulator)) {
                accumulator = new CreditRelationshipAccumulator(person, sortOrder);
                linkedCredits[person.Id] = accumulator;
            }

            accumulator.Add(role, character, sortOrder);
        }

        foreach (var credit in linkedCredits.Values.OrderBy(credit => credit.SortOrder).ThenBy(credit => credit.Person.Title)) {
            var metadata = JsonSerializer.Serialize(new {
                role = credit.Role,
                character = credit.Character,
                roles = credit.Roles,
                characters = credit.Characters.Count == 0 ? null : credit.Characters
            });
            AddRelationship(entityId, "cast", "Cast", credit.Person.Id, credit.Person.KindCode, credit.SortOrder, metadata, now);
        }
    }

    private sealed class CreditRelationshipAccumulator {
        public CreditRelationshipAccumulator(EntityRow person, int sortOrder) {
            Person = person;
            SortOrder = sortOrder;
        }

        public EntityRow Person { get; }

        public int SortOrder { get; private set; }

        public string? Role { get; private set; }

        public string? Character { get; private set; }

        public List<string> Roles { get; } = [];

        public List<string> Characters { get; } = [];

        public void Add(string role, string? character, int sortOrder) {
            if (sortOrder < SortOrder) {
                SortOrder = sortOrder;
            }

            Role ??= role;
            AddDistinct(Roles, role);

            if (!string.IsNullOrWhiteSpace(character)) {
                Character ??= character;
                AddDistinct(Characters, character);
            }
        }

        private static void AddDistinct(List<string> values, string value) {
            if (!values.Contains(value, StringComparer.OrdinalIgnoreCase)) {
                values.Add(value);
            }
        }
    }

    private async Task RemoveRelationshipAsync(Guid entityId, string code, CancellationToken cancellationToken) {
        var existing = await _db.EntityRelationshipLinks
            .Where(row => row.EntityId == entityId && row.RelationshipCode == code)
            .ToArrayAsync(cancellationToken);
        _db.EntityRelationshipLinks.RemoveRange(existing);
    }

    private void AddRelationship(
        Guid entityId,
        string code,
        string label,
        Guid targetEntityId,
        string targetKindCode,
        int sortOrder,
        string? metadataJson,
        DateTimeOffset now) {
        _db.EntityRelationshipLinks.Add(new EntityRelationshipLinkRow {
            EntityId = entityId,
            RelationshipCode = code,
            Label = label,
            TargetEntityId = targetEntityId,
            TargetKindCode = targetKindCode,
            SortOrder = sortOrder,
            MetadataJson = metadataJson,
            CreatedAt = now
        });
    }

    private async Task UpsertDatesAsync(Guid entityId, IReadOnlyDictionary<string, string> dates, DateTimeOffset now, CancellationToken cancellationToken) {
        foreach (var (code, value) in dates.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))) {
            var existing = await _db.EntityDates.FindAsync([entityId, code], cancellationToken);
            if (existing is null) {
                _db.EntityDates.Add(new EntityDateRow { EntityId = entityId, Code = code, Value = value, SortableValue = ParseDateOnly(value), UpdatedAt = now });
            } else {
                existing.Value = value;
                existing.SortableValue = ParseDateOnly(value);
                existing.UpdatedAt = now;
            }
        }
    }

    private async Task ReplaceDatesAsync(Guid entityId, IReadOnlyDictionary<string, string> dates, DateTimeOffset now, CancellationToken cancellationToken) {
        var existing = await _db.EntityDates
            .Where(row => row.EntityId == entityId)
            .ToArrayAsync(cancellationToken);
        _db.EntityDates.RemoveRange(existing);
        await UpsertDatesAsync(entityId, dates, now, cancellationToken);
    }

    private async Task UpsertStatsAsync(Guid entityId, IReadOnlyDictionary<string, int> stats, DateTimeOffset now, CancellationToken cancellationToken) {
        foreach (var (code, value) in FilterStats(stats)) {
            var existing = await _db.EntityStats.FindAsync([entityId, code], cancellationToken);
            if (existing is null) {
                _db.EntityStats.Add(new EntityStatRow { EntityId = entityId, Code = code, Value = value, UpdatedAt = now });
            } else {
                existing.Value = value;
                existing.UpdatedAt = now;
            }
        }
    }

    private async Task ReplaceStatsAsync(Guid entityId, IReadOnlyDictionary<string, int> stats, DateTimeOffset now, CancellationToken cancellationToken) {
        var existing = await _db.EntityStats
            .Where(row => row.EntityId == entityId)
            .ToArrayAsync(cancellationToken);
        _db.EntityStats.RemoveRange(existing);
        await UpsertStatsAsync(entityId, stats, now, cancellationToken);
    }

    private static IReadOnlyDictionary<string, int> FilterStats(IReadOnlyDictionary<string, int> stats) =>
        stats
            .Where(item => !IgnoredStatCodes.Contains(item.Key.Trim()))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

    private async Task UpsertPositionsAsync(EntityRow entity, IReadOnlyDictionary<string, int> positions, DateTimeOffset now, CancellationToken cancellationToken) {
        foreach (var (code, value) in positions) {
            var existing = await _db.EntityPositions.FindAsync([entity.Id, code], cancellationToken);
            if (existing is null) {
                _db.EntityPositions.Add(new EntityPositionRow { EntityId = entity.Id, Code = code, Value = value, UpdatedAt = now });
            } else {
                existing.Value = value;
                existing.UpdatedAt = now;
            }
        }

        await ApplyStructuralSortOrderAsync(entity, positions, now, cancellationToken);
    }

    private async Task ReplacePositionsAsync(EntityRow entity, IReadOnlyDictionary<string, int> positions, DateTimeOffset now, CancellationToken cancellationToken) {
        var existing = await _db.EntityPositions
            .Where(row => row.EntityId == entity.Id)
            .ToArrayAsync(cancellationToken);
        _db.EntityPositions.RemoveRange(existing);
        await UpsertPositionsAsync(entity, positions, now, cancellationToken);
    }

    private async Task ApplyStructuralSortOrderAsync(
        EntityRow entity,
        IReadOnlyDictionary<string, int> positions,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var sortOrder = StructuralSortOrder(entity.KindCode, positions);
        if (sortOrder is null) {
            return;
        }

        entity.SortOrder = sortOrder.Value;
        entity.UpdatedAt = now;
    }

    private static int? StructuralSortOrder(string kindCode, IReadOnlyDictionary<string, int> positions) {
        if (kindCode.Equals(EntityKindRegistry.VideoSeason.Code, StringComparison.OrdinalIgnoreCase)) {
            return PositionValue(positions, "season", "sort");
        }

        if (kindCode.Equals(EntityKindRegistry.Video.Code, StringComparison.OrdinalIgnoreCase)) {
            return PositionValue(positions, "episode", "absolute-episode", "sort");
        }

        return PositionValue(positions, "track", "page", "chapter", "volume", "sort");
    }

    private static int? PositionValue(IReadOnlyDictionary<string, int> positions, params string[] codes) {
        foreach (var code in codes) {
            if (positions.TryGetValue(code, out var value)) {
                return value;
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, int> NormalizePositions(IReadOnlyDictionary<string, int> positions) {
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, value) in positions) {
            normalized[NormalizePositionCode(code)] = value;
        }

        return normalized;
    }

    private static string NormalizePositionCode(string code) => code.Trim() switch {
        var value when value.Equals("seasonNumber", StringComparison.OrdinalIgnoreCase) => "season",
        var value when value.Equals("episodeNumber", StringComparison.OrdinalIgnoreCase) => "episode",
        var value when value.Equals("absoluteEpisodeNumber", StringComparison.OrdinalIgnoreCase) => "absolute-episode",
        var value when value.Equals("volumeNumber", StringComparison.OrdinalIgnoreCase) => "volume",
        var value when value.Equals("chapterNumber", StringComparison.OrdinalIgnoreCase) => "chapter",
        var value when value.Equals("pageNumber", StringComparison.OrdinalIgnoreCase) => "page",
        var value when value.Equals("trackNumber", StringComparison.OrdinalIgnoreCase) => "track",
        var value when value.Equals("sortOrder", StringComparison.OrdinalIgnoreCase) => "sort",
        var value => value
    };

    private async Task UpsertClassificationAsync(Guid entityId, string? value, DateTimeOffset now, CancellationToken cancellationToken) {
        var existing = await _db.EntityClassifications.FindAsync([entityId], cancellationToken);
        if (existing is null) {
            _db.EntityClassifications.Add(new EntityClassificationRow { EntityId = entityId, Value = value, System = "plugin", UpdatedAt = now });
        } else {
            existing.Value = value;
            existing.System = "plugin";
            existing.UpdatedAt = now;
        }
    }

    private async Task ReplaceClassificationAsync(Guid entityId, string? value, DateTimeOffset now, CancellationToken cancellationToken) {
        var existing = await _db.EntityClassifications.FindAsync([entityId], cancellationToken);
        if (string.IsNullOrWhiteSpace(value)) {
            if (existing is not null) {
                _db.EntityClassifications.Remove(existing);
            }
            return;
        }

        if (existing is null) {
            _db.EntityClassifications.Add(new EntityClassificationRow { EntityId = entityId, Value = value.Trim(), System = "manual", UpdatedAt = now });
        } else {
            existing.Value = value.Trim();
            existing.System = "manual";
            existing.UpdatedAt = now;
        }
    }

    private async Task UpsertRatingAsync(Guid entityId, int? value, DateTimeOffset now, CancellationToken cancellationToken) {
        var row = await _db.Entities.FindAsync([entityId], cancellationToken);
        if (row is null) return;
        row.RatingValue = value;
        row.UpdatedAt = now;
    }

    private async Task UpsertFlagsAsync(Guid entityId, EntityMetadataFlagsPatch? patch, DateTimeOffset now, CancellationToken cancellationToken) {
        if (patch is null) return;
        var row = await _db.Entities.FindAsync([entityId], cancellationToken);
        if (row is null) return;
        if (patch.IsFavorite.HasValue) row.IsFavorite = patch.IsFavorite.Value;
        if (patch.IsNsfw.HasValue) row.IsNsfw = patch.IsNsfw.Value;
        if (patch.IsOrganized.HasValue) row.IsOrganized = patch.IsOrganized.Value;
        row.UpdatedAt = now;
    }

    private async Task DownloadSelectedImagesAsync(
        Guid entityId,
        IReadOnlyDictionary<string, string?> selectedImages,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        foreach (var (roleCode, url) in selectedImages) {
            if (string.IsNullOrWhiteSpace(url) || !roleCode.TryDecodeAs<EntityFileRole>(out var role)) {
                continue;
            }

            var bytes = await _http.GetByteArrayAsync(url, cancellationToken);
            var ext = ExtensionFromUrl(url);
            var relativePath = Path.Combine("plugins", "artwork", entityId.ToString(), $"{roleCode}-{ShortHash(url)}{ext}");
            var physicalPath = Path.Combine(_options.CacheRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
            await File.WriteAllBytesAsync(physicalPath, bytes, cancellationToken);

            var publicPath = $"/assets/{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";
            var existing = await FindEntityFileAsync(entityId, role, cancellationToken);
            if (existing is null) {
                _db.EntityFiles.Add(new EntityFileRow {
                    Id = Guid.NewGuid(),
                    EntityId = entityId,
                    Role = role,
                    Path = publicPath,
                    MimeType = MimeTypeFromExtension(ext),
                    Source = "custom",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            } else {
                existing.Path = publicPath;
                existing.MimeType = MimeTypeFromExtension(ext);
                existing.Source = "custom";
                existing.UpdatedAt = now;
            }
        }
    }

    /// <summary>
    /// Applies metadata and artwork from relationship proposals into linked Person and Studio entities
    /// that were created or resolved during credits/studio apply.
    /// </summary>
    private async Task ApplyRelationshipProposalsAsync(
        Guid sourceEntityId,
        IReadOnlyList<EntityMetadataProposal> relationships,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        foreach (var child in relationships) {
            if (string.IsNullOrWhiteSpace(child.Patch.Title)) {
                continue;
            }

            if (child.TargetKind is not ("person" or "studio" or "tag")) {
                continue;
            }

            var linkedEntity = await FindEntityByKindAndTitleAsync(child.TargetKind, child.Patch.Title.Trim(), cancellationToken);
            if (linkedEntity is null) {
                continue;
            }

            if (linkedEntity.Id == sourceEntityId) {
                continue;
            }

            await ApplyPatchToEntityAsync(linkedEntity, child.Patch, [], now, cancellationToken);

            if (child.Images.Count == 0) {
                continue;
            }

            var image = child.Images.FirstOrDefault(img => img.Kind is "poster") ?? child.Images.FirstOrDefault(img => img.Kind is "logo") ?? child.Images[0];
            var role = child.TargetKind == "studio" ? EntityFileRole.Logo : EntityFileRole.Poster;

            await DownloadPluginImageAsync(linkedEntity, image, role, now, cancellationToken);
        }
    }

    private async Task DownloadPluginImageAsync(
        EntityRow entity,
        ImageCandidate image,
        EntityFileRole role,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        try {
            var bytes = await _http.GetByteArrayAsync(image.Url, cancellationToken);
            var ext = ExtensionFromUrl(image.Url);
            var relativePath = Path.Combine("plugins", "artwork", entity.Id.ToString(), $"{role.ToString().ToLowerInvariant()}-{ShortHash(image.Url)}{ext}");
            var physicalPath = Path.Combine(_options.CacheRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
            await File.WriteAllBytesAsync(physicalPath, bytes, cancellationToken);

            var publicPath = $"/assets/{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";
            var existing = await FindEntityFileAsync(entity.Id, role, cancellationToken);
            if (existing is null) {
                _db.EntityFiles.Add(new EntityFileRow {
                    Id = Guid.NewGuid(),
                    EntityId = entity.Id,
                    Role = role,
                    Path = publicPath,
                    MimeType = MimeTypeFromExtension(ext),
                    Source = "custom",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            } else {
                existing.Path = publicPath;
                existing.MimeType = MimeTypeFromExtension(ext);
                existing.Source = "custom";
                existing.UpdatedAt = now;
            }

            entity.UpdatedAt = now;
        } catch (HttpRequestException) {
        }
    }

    /// <summary>
    /// Applies cascade metadata patch fields to an existing child entity.
    /// </summary>
    private async Task ApplyStructuralChildrenAsync(
        IReadOnlyList<EntityMetadataProposal> children,
        DateTimeOffset now,
        HashSet<Guid> visited,
        CancellationToken cancellationToken) {
        foreach (var child in children) {
            if (child.TargetEntityId is null) {
                continue;
            }

            if (!visited.Add(child.TargetEntityId.Value)) {
                continue;
            }

            var childEntity = await _db.Entities
                .FirstOrDefaultAsync(row => row.Id == child.TargetEntityId.Value && row.DeletedAt == null, cancellationToken);
            if (childEntity is null) {
                visited.Remove(child.TargetEntityId.Value);
                continue;
            }

            await ApplyPatchToEntityAsync(childEntity, child.Patch, child.Images, now, cancellationToken);
            var relationshipProposals = RelationshipProposals(child);
            if (relationshipProposals.Count > 0 &&
                (child.Patch.Credits.Count > 0 || !string.IsNullOrWhiteSpace(child.Patch.Studio) || child.Patch.Tags.Count > 0)) {
                await ApplyRelationshipProposalsAsync(childEntity.Id, relationshipProposals, now, cancellationToken);
            }

            await ApplyStructuralChildrenAsync(StructuralChildProposals(child), now, visited, cancellationToken);
            visited.Remove(child.TargetEntityId.Value);
        }
    }

    private async Task ApplyPatchToEntityAsync(
        EntityRow entity,
        EntityMetadataPatch patch,
        IReadOnlyList<ImageCandidate> images,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(patch.Title)) {
            entity.Title = patch.Title.Trim();
        }

        if (!string.IsNullOrWhiteSpace(patch.Description)) {
            await UpsertDescriptionAsync(entity.Id, patch.Description, now, cancellationToken);
        }

        if (patch.ExternalIds.Count > 0) {
            await UpsertExternalIdsAsync(entity.Id, patch.ExternalIds, patch.Urls, now, cancellationToken);
        }

        if (patch.Urls.Count > 0) {
            await UpsertUrlsAsync(entity.Id, patch.Urls, now, cancellationToken);
        }

        if (patch.Tags.Count > 0) {
            await ReplaceTagsAsync(entity.Id, patch.Tags, now, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(patch.Studio)) {
            await SetStudioAsync(entity.Id, patch.Studio, now, cancellationToken);
        }

        if (patch.Credits.Count > 0) {
            await ReplaceCreditsAsync(entity.Id, patch.Credits, now, cancellationToken);
        }

        if (patch.Dates.Count > 0) {
            await UpsertDatesAsync(entity.Id, patch.Dates, now, cancellationToken);
        }

        if (patch.Stats.Count > 0) {
            await UpsertStatsAsync(entity.Id, patch.Stats, now, cancellationToken);
        }

        if (patch.Positions.Count > 0) {
            var normalizedPositions = NormalizePositions(patch.Positions);
            await UpsertPositionsAsync(entity, normalizedPositions, now, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(patch.Classification)) {
            await UpsertClassificationAsync(entity.Id, patch.Classification, now, cancellationToken);
        }

        if (patch.Flags is not null) {
            await UpsertFlagsAsync(entity.Id, patch.Flags, now, cancellationToken);
        }

        if (images.Count > 0) {
            var image = images.FirstOrDefault(i => i.Kind is "still") ?? images.FirstOrDefault(i => i.Kind is "poster") ?? images[0];
            var role = image.Kind switch {
                "still" => EntityFileRole.Thumbnail,
                "poster" => EntityFileRole.Poster,
                _ => EntityFileRole.Thumbnail
            };
            await DownloadPluginImageAsync(entity, image, role, now, cancellationToken);
        }

        entity.UpdatedAt = now;
    }

    private async Task<EntityFileRow?> FindEntityFileAsync(Guid entityId, EntityFileRole role, CancellationToken cancellationToken) =>
        _db.EntityFiles.Local.FirstOrDefault(row => row.EntityId == entityId && row.Role == role)
        ?? await _db.EntityFiles.FirstOrDefaultAsync(row => row.EntityId == entityId && row.Role == role, cancellationToken);

    private static IReadOnlyList<EntityMetadataProposal> StructuralChildProposals(EntityMetadataProposal proposal) =>
        proposal.Children
            .Where(child => !IsRelationshipMetadataKind(child.TargetKind))
            .ToArray();

    private static IReadOnlyList<EntityMetadataProposal> RelationshipProposals(EntityMetadataProposal proposal) {
        var relationships = new List<EntityMetadataProposal>();
        if (proposal.Relationships is { Count: > 0 }) {
            relationships.AddRange(proposal.Relationships);
        }

        relationships.AddRange(proposal.Children.Where(child => IsRelationshipMetadataKind(child.TargetKind)));

        return relationships
            .GroupBy(child => child.ProposalId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool IsRelationshipMetadataKind(string kind) =>
        kind is "person" or "studio" or "tag";

    private static bool IsKindCompatible(string entityKind, string expectedKind) =>
        entityKind.Equals(expectedKind, StringComparison.OrdinalIgnoreCase) ||
        (entityKind.Equals(EntityKindRegistry.Video.Code, StringComparison.OrdinalIgnoreCase) &&
            expectedKind.Equals("video-episode", StringComparison.OrdinalIgnoreCase));

    private async Task<EntityRow?> FindEntityByKindAndTitleAsync(string kind, string title, CancellationToken cancellationToken) =>
        _db.Entities.Local.FirstOrDefault(
            row => row.KindCode == kind && row.Title.Equals(title, StringComparison.OrdinalIgnoreCase) && row.DeletedAt == null)
        ?? await _db.Entities.FirstOrDefaultAsync(
            row => row.KindCode == kind && row.Title.ToLower() == title.ToLower() && row.DeletedAt == null,
            cancellationToken);

    private EntityRow CreateEntity(string kind, string title, DateTimeOffset now) {
        var entity = new EntityRow {
            Id = Guid.NewGuid(),
            KindCode = kind,
            Title = title,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private static DateOnly? ParseDateOnly(string value) =>
        DateOnly.TryParse(value, out var parsed) ? parsed : null;

    private static string ShortHash(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();

    private static string ExtensionFromUrl(string url) {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        var ext = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(ext) ? ".jpg" : ext.ToLowerInvariant();
    }

    private static string? MimeTypeFromExtension(string ext) =>
        ext.ToLowerInvariant() switch {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null
        };
}
