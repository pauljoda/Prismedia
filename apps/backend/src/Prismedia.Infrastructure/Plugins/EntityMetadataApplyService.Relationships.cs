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
        EntityKindRegistry.Movie.Code,
        EntityKindRegistry.MusicArtist.Code,
        EntityKindRegistry.Video.Code,
        EntityKindRegistry.VideoSeries.Code
    };

    private async Task ApplyScopedRelationshipFieldsAsync(
        EntityRow entity,
        ISet<string> fields,
        EntityMetadataPatch patch,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (!fields.Contains(MetadataPatchField.Tags.ToCode()) && !fields.Contains(MetadataPatchField.Studio.ToCode()) && !fields.Contains(MetadataPatchField.Credits.ToCode())) {
            return;
        }

        var owner = await ResolveRelationshipOwnerAsync(entity, cancellationToken);
        if (owner is null) {
            return;
        }

        if (fields.Contains(MetadataPatchField.Tags.ToCode())) {
            await ReplaceTagsAsync(owner.Id, patch.Tags, now, markNsfw: false, cancellationToken);
        }

        if (fields.Contains(MetadataPatchField.Studio.ToCode())) {
            await RemoveRelationshipAsync(owner.Id, RelationshipKind.Studio.ToCode(), cancellationToken);
            if (!string.IsNullOrWhiteSpace(patch.Studio)) {
                await SetStudioAsync(owner.Id, patch.Studio, now, markNsfw: false, cancellationToken);
            }
        }

        if (fields.Contains(MetadataPatchField.Credits.ToCode())) {
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
        if (!selected.Contains(MetadataPatchField.Tags.ToCode()) &&
            !(selected.Contains(MetadataPatchField.Studio.ToCode()) && !string.IsNullOrWhiteSpace(patch.Studio)) &&
            !selected.Contains(MetadataPatchField.Credits.ToCode())) {
            return;
        }

        var owner = await ResolveRelationshipOwnerAsync(entity, cancellationToken);
        if (owner is null) {
            return;
        }

        if (selected.Contains(MetadataPatchField.Tags.ToCode())) {
            await ReplaceTagsAsync(owner.Id, patch.Tags, now, markNsfw, cancellationToken);
        }

        if (selected.Contains(MetadataPatchField.Studio.ToCode()) && !string.IsNullOrWhiteSpace(patch.Studio)) {
            await SetStudioAsync(owner.Id, patch.Studio, now, markNsfw, cancellationToken);
        }

        if (selected.Contains(MetadataPatchField.Credits.ToCode())) {
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

            var parent = _db.Entities.Local.FirstOrDefault(row => row.Id == parentId)
                ?? await _db.Entities.FirstOrDefaultAsync(row => row.Id == parentId, cancellationToken);
            if (parent is null) {
                return null;
            }

            current = parent;
        }

        return null;
    }

    private async Task ReplaceTagsAsync(Guid entityId, IReadOnlyList<string> tags, DateTimeOffset now, bool markNsfw, CancellationToken cancellationToken) {
        await RemoveRelationshipAsync(entityId, RelationshipKind.Tags.ToCode(), cancellationToken);

        var order = 0;
        foreach (var name in tags.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)) {
            var tag = await FindEntityByTitleAsync("tag", name, parentEntityId: null, cancellationToken)
                ?? CreateEntity("tag", name, now);
            MarkNsfwIfRequested(tag, markNsfw, now);
            AddRelationship(entityId, RelationshipKind.Tags.ToCode(), "Tags", tag.Id, tag.KindCode, order++, null, now);
        }
    }

    private async Task SetStudioAsync(Guid entityId, string studioName, DateTimeOffset now, bool markNsfw, CancellationToken cancellationToken) {
        var studio = await FindEntityByTitleAsync(EntityKind.Studio.ToCode(), studioName.Trim(), parentEntityId: null, cancellationToken)
            ?? CreateEntity(EntityKind.Studio.ToCode(), studioName.Trim(), now);
        MarkNsfwIfRequested(studio, markNsfw, now);
        await RemoveRelationshipAsync(entityId, RelationshipKind.Studio.ToCode(), cancellationToken);
        AddRelationship(entityId, RelationshipKind.Studio.ToCode(), "Studio", studio.Id, studio.KindCode, 0, null, now);
    }

    private async Task ReplaceCreditsAsync(Guid entityId, IReadOnlyList<CreditPatch> credits, DateTimeOffset now, bool markNsfw, CancellationToken cancellationToken) {
        await RemoveRelationshipAsync(entityId, RelationshipKind.Cast.ToCode(), cancellationToken);

        var order = 0;
        var resolvedPeople = new Dictionary<string, EntityRow>(StringComparer.OrdinalIgnoreCase);
        var linkedCredits = new Dictionary<Guid, CreditRelationshipAccumulator>();
        foreach (var credit in credits.Where(credit => !string.IsNullOrWhiteSpace(credit.Name))) {
            var personName = credit.Name.Trim();
            if (!resolvedPeople.TryGetValue(personName, out var person)) {
                person = await FindEntityByTitleAsync("person", personName, parentEntityId: null, cancellationToken)
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
            AddRelationship(entityId, RelationshipKind.Cast.ToCode(), "Cast", credit.Person.Id, credit.Person.KindCode, credit.SortOrder, metadata, now);
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
        // The composite key is (EntityId, RelationshipCode, TargetEntityId). The same link can be
        // produced more than once in a single apply — a remove pass marks the persisted row Deleted
        // before it is re-added, and several entities in a tree (e.g. a book's volumes and chapters)
        // resolve their relationship owner to the same parent and each re-apply its credits/tags. EF
        // cannot track two instances of the same key, so reconcile against the change tracker: update
        // the already-tracked link in place (reviving a Deleted one) rather than adding a duplicate.
        var tracked = _db.ChangeTracker.Entries<EntityRelationshipLinkRow>()
            .FirstOrDefault(entry =>
                entry.Entity.EntityId == entityId &&
                entry.Entity.RelationshipCode == code &&
                entry.Entity.TargetEntityId == targetEntityId);
        if (tracked is not null) {
            tracked.Entity.Label = label;
            tracked.Entity.TargetKindCode = targetKindCode;
            tracked.Entity.SortOrder = sortOrder;
            tracked.Entity.MetadataJson = metadataJson;
            if (tracked.State == EntityState.Deleted) {
                // The row exists in the database; revive and update it instead of delete-then-insert.
                tracked.Entity.CreatedAt = now;
                tracked.State = EntityState.Modified;
            }
            // An Added entry stays Added (its updated values are inserted); Unchanged becomes Modified
            // automatically via property change detection.
            return;
        }

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

    private async Task ApplyRelationshipArtworkAsync(
        EntityRow linkedEntity,
        EntityMetadataProposal proposal,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (proposal.Images.Count == 0) {
            return;
        }

        if (proposal.TargetKind == ProposalKind.Studio) {
            var logo = ImageKindRoleResolver.Pick(proposal.Images, MediaImageKind.Logo, MediaImageKind.Poster)
                ?? proposal.Images[0];
            await _artwork.DownloadPluginImageAsync(linkedEntity, logo, EntityFileRole.Logo, now, cancellationToken);

            var backdrop = ImageKindRoleResolver.Pick(
                proposal.Images, MediaImageKind.Backdrop, MediaImageKind.Banner, MediaImageKind.Hero);
            if (backdrop is not null) {
                await _artwork.DownloadPluginImageAsync(linkedEntity, backdrop, EntityFileRole.Backdrop, now, cancellationToken);
            }

            return;
        }

        var image = ImageKindRoleResolver.Pick(proposal.Images, MediaImageKind.Poster, MediaImageKind.Logo)
            ?? proposal.Images[0];
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
