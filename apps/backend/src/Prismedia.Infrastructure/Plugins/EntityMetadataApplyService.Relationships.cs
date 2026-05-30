using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class EntityMetadataApplyService {
    private static readonly HashSet<string> RelationshipOwnerKindCodes = new(StringComparer.OrdinalIgnoreCase) {
        EntityKindRegistry.Audio.Code,
        EntityKindRegistry.AudioLibrary.Code,
        EntityKindRegistry.AudioTrack.Code,
        EntityKindRegistry.Book.Code,
        EntityKindRegistry.Gallery.Code,
        EntityKindRegistry.Image.Code,
        EntityKindRegistry.Video.Code,
        EntityKindRegistry.VideoSeries.Code
    };

    private async Task ApplyScopedRelationshipFieldsAsync(
        EntityRow entity,
        ISet<string> fields,
        EntityMetadataPatch patch,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (!fields.Contains("tags") && !fields.Contains("studio") && !fields.Contains("credits")) {
            return;
        }

        var owner = await ResolveRelationshipOwnerAsync(entity, cancellationToken);
        if (owner is null) {
            return;
        }

        if (fields.Contains("tags")) {
            await ReplaceTagsAsync(owner.Id, patch.Tags, now, markNsfw: false, cancellationToken);
        }

        if (fields.Contains("studio")) {
            await RemoveRelationshipAsync(owner.Id, "studio", cancellationToken);
            if (!string.IsNullOrWhiteSpace(patch.Studio)) {
                await SetStudioAsync(owner.Id, patch.Studio, now, markNsfw: false, cancellationToken);
            }
        }

        if (fields.Contains("credits")) {
            await ReplaceCreditsAsync(owner.Id, patch.Credits, now, markNsfw: false, cancellationToken);
        }

        owner.UpdatedAt = now;
    }

    private async Task ApplySelectedRelationshipFieldsAsync(
        EntityRow entity,
        ISet<string> selected,
        EntityMetadataPatch patch,
        DateTimeOffset now,
        bool markNsfw,
        CancellationToken cancellationToken) {
        if (!selected.Contains("tags") &&
            !(selected.Contains("studio") && !string.IsNullOrWhiteSpace(patch.Studio)) &&
            !selected.Contains("credits")) {
            return;
        }

        var owner = await ResolveRelationshipOwnerAsync(entity, cancellationToken);
        if (owner is null) {
            return;
        }

        if (selected.Contains("tags")) {
            await ReplaceTagsAsync(owner.Id, patch.Tags, now, markNsfw, cancellationToken);
        }

        if (selected.Contains("studio") && !string.IsNullOrWhiteSpace(patch.Studio)) {
            await SetStudioAsync(owner.Id, patch.Studio, now, markNsfw, cancellationToken);
        }

        if (selected.Contains("credits")) {
            await ReplaceCreditsAsync(owner.Id, patch.Credits, now, markNsfw, cancellationToken);
        }

        owner.UpdatedAt = now;
    }

    private async Task ApplyCascadeRelationshipFieldsAsync(
        EntityRow entity,
        EntityMetadataPatch patch,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (patch.Tags.Count == 0 && string.IsNullOrWhiteSpace(patch.Studio) && patch.Credits.Count == 0) {
            return;
        }

        var owner = await ResolveRelationshipOwnerAsync(entity, cancellationToken);
        if (owner is null) {
            return;
        }

        // Cascade children carry the provider's NSFW flag in their patch; propagate it to the
        // people, studio, and tags created beneath them.
        var markNsfw = patch.Flags?.IsNsfw == true;

        if (patch.Tags.Count > 0) {
            await ReplaceTagsAsync(owner.Id, patch.Tags, now, markNsfw, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(patch.Studio)) {
            await SetStudioAsync(owner.Id, patch.Studio, now, markNsfw, cancellationToken);
        }

        if (patch.Credits.Count > 0) {
            await ReplaceCreditsAsync(owner.Id, patch.Credits, now, markNsfw, cancellationToken);
        }

        owner.UpdatedAt = now;
    }

    private async Task<EntityRow?> ResolveRelationshipOwnerAsync(EntityRow entity, CancellationToken cancellationToken) {
        var current = entity;
        var visited = new HashSet<Guid>();
        while (visited.Add(current.Id)) {
            if (RelationshipOwnerKindCodes.Contains(current.KindCode)) {
                return current;
            }

            if (current.ParentEntityId is not { } parentId) {
                return null;
            }

            var parent = _db.Entities.Local.FirstOrDefault(row => row.Id == parentId && row.DeletedAt == null)
                ?? await _db.Entities.FirstOrDefaultAsync(row => row.Id == parentId && row.DeletedAt == null, cancellationToken);
            if (parent is null) {
                return null;
            }

            current = parent;
        }

        return null;
    }

    private async Task ReplaceTagsAsync(Guid entityId, IReadOnlyList<string> tags, DateTimeOffset now, bool markNsfw, CancellationToken cancellationToken) {
        await RemoveRelationshipAsync(entityId, "tags", cancellationToken);

        var order = 0;
        foreach (var name in tags.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)) {
            var tag = await FindEntityByKindAndTitleAsync("tag", name, cancellationToken)
                ?? CreateEntity("tag", name, now);
            MarkNsfwIfRequested(tag, markNsfw, now);
            AddRelationship(entityId, "tags", "Tags", tag.Id, tag.KindCode, order++, null, now);
        }
    }

    private async Task SetStudioAsync(Guid entityId, string studioName, DateTimeOffset now, bool markNsfw, CancellationToken cancellationToken) {
        var studio = await FindEntityByKindAndTitleAsync("studio", studioName.Trim(), cancellationToken)
            ?? CreateEntity("studio", studioName.Trim(), now);
        MarkNsfwIfRequested(studio, markNsfw, now);
        await RemoveRelationshipAsync(entityId, "studio", cancellationToken);
        AddRelationship(entityId, "studio", "Studio", studio.Id, studio.KindCode, 0, null, now);
    }

    private async Task ReplaceCreditsAsync(Guid entityId, IReadOnlyList<CreditPatch> credits, DateTimeOffset now, bool markNsfw, CancellationToken cancellationToken) {
        await RemoveRelationshipAsync(entityId, "cast", cancellationToken);

        var order = 0;
        var resolvedPeople = new Dictionary<string, EntityRow>(StringComparer.OrdinalIgnoreCase);
        var linkedCredits = new Dictionary<Guid, CreditRelationshipAccumulator>();
        foreach (var credit in credits.Where(credit => !string.IsNullOrWhiteSpace(credit.Name))) {
            var personName = credit.Name.Trim();
            if (!resolvedPeople.TryGetValue(personName, out var person)) {
                person = await FindEntityByKindAndTitleAsync("person", personName, cancellationToken)
                    ?? CreateEntity("person", personName, now);
                MarkNsfwIfRequested(person, markNsfw, now);
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

    /// <summary>
    /// Marks a related entity (person, studio, or tag) NSFW when an NSFW provider created or
    /// linked it, so adult metadata never leaks a clean classification onto its relations.
    /// </summary>
    private static void MarkNsfwIfRequested(EntityRow entity, bool markNsfw, DateTimeOffset now) {
        if (!markNsfw || entity.IsNsfw == true) {
            return;
        }

        entity.IsNsfw = true;
        entity.UpdatedAt = now;
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

    /// <summary>
    /// Applies metadata and artwork from relationship proposals into linked Person and Studio entities
    /// that were created or resolved during credits/studio apply.
    /// </summary>
    private async Task ApplyRelationshipProposalsAsync(
        Guid sourceEntityId,
        IReadOnlyList<EntityMetadataProposal> relationships,
        DateTimeOffset now,
        IReadOnlyList<string> sourcePath,
        IdentifyApplyProgressReporter? progress,
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

            var title = child.Patch.Title.Trim();
            var path = sourcePath.Count == 0 ? [title] : sourcePath.Concat([title]).ToArray();
            progress?.ReportEntity(linkedEntity.KindCode, title, path);

            await ApplyPatchToEntityAsync(linkedEntity, child.Patch, [], now, cancellationToken);

            await ApplyRelationshipArtworkAsync(linkedEntity, child, now, cancellationToken);
        }
    }

    private async Task ApplyRelationshipArtworkAsync(
        EntityRow linkedEntity,
        EntityMetadataProposal proposal,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (proposal.Images.Count == 0) {
            return;
        }

        if (proposal.TargetKind == "studio") {
            var logo = proposal.Images.FirstOrDefault(image => image.Kind is "logo" or "poster") ?? proposal.Images[0];
            await _artwork.DownloadPluginImageAsync(linkedEntity, logo, EntityFileRole.Logo, now, cancellationToken);

            var backdrop = proposal.Images.FirstOrDefault(image => image.Kind is "backdrop" or "banner" or "hero");
            if (backdrop is not null) {
                await _artwork.DownloadPluginImageAsync(linkedEntity, backdrop, EntityFileRole.Backdrop, now, cancellationToken);
            }

            return;
        }

        var image = proposal.Images.FirstOrDefault(img => img.Kind is "poster") ??
            proposal.Images.FirstOrDefault(img => img.Kind is "logo") ??
            proposal.Images[0];
        await _artwork.DownloadPluginImageAsync(linkedEntity, image, EntityFileRole.Poster, now, cancellationToken);
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
}
