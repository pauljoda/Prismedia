using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Entities;

/// <summary>
/// Coordinates the projection from a hydrated domain <see cref="Entity"/> to the API
/// <see cref="EntityCard"/> contract. Capability mapping is delegated to the typed
/// <see cref="EntityCapabilityProjectionRegistry"/>; the row-based browse/thumbnail path
/// in Infrastructure is the one deliberate read-optimized exception.
/// </summary>
public static class EntityCardProjector {
    /// <summary>
    /// Projects an entity using canonical source-ownership truth supplied by its read boundary. Requiring
    /// that fact prevents shallow incidental hydration from silently dropping descendant file management.
    /// </summary>
    public static EntityCard ToCard(
        Entity entity,
        bool hasSourceBackedSubtree,
        Guid? currentUserId = null,
        IReadOnlyList<EntityCreditMetadata>? creditMetadata = null) =>
        ToCard(
            entity,
            new EntityFileManagementState(hasSourceBackedSubtree, HasRecoverableDeletion: false),
            currentUserId,
            creditMetadata);

    /// <summary>
    /// Projects an Entity using canonical managed-file state supplied by its read boundary. Recoverable
    /// deletion state keeps the action available after source rows are gone without reporting those rows
    /// as source media or enabling deletion for an ordinary fileless Wanted Entity.
    /// </summary>
    public static EntityCard ToCard(
        Entity entity,
        EntityFileManagementState fileManagementState,
        Guid? currentUserId = null,
        IReadOnlyList<EntityCreditMetadata>? creditMetadata = null) =>
        new() {
            Id = entity.Id,
            Kind = entity.Kind,
            Title = entity.Title,
            ParentEntityId = entity.ParentEntityId,
            SortOrder = entity.SortOrder,
            HasSourceMedia = fileManagementState.HasSourceBackedSubtree,
            Capabilities = EntityCapabilityProjectionRegistry.Project(
                entity,
                fileManagementState,
                currentUserId,
                creditMetadata),
            ChildrenByKind = ToGroups(entity.ChildrenByKind),
            Relationships = ToGroups(entity.RelationshipsByKind),
        };

    private static IReadOnlyList<EntityGroup> ToGroups(
        IReadOnlyDictionary<EntityKind, IReadOnlyList<Entity>> map) =>
        map.Select(pair => new EntityGroup(
                pair.Key,
                EntityKindRegistry.Describe(pair.Key).GroupLabel,
                pair.Value
                    .OrderBy(child => child.SortOrder ?? int.MaxValue)
                    .ThenBy(child => child.Title)
                    .Select(ToThumbnail)
                    .ToArray()))
            .ToArray();

    private static EntityThumbnail ToThumbnail(Entity entity) {
        var artwork = EntityArtworkProjection.Project(entity);

        return new EntityThumbnail(
            entity.Id,
            entity.Kind,
            entity.Title,
            entity.ParentEntityId,
            entity.SortOrder,
            artwork.CoverUrl,
            artwork.ThumbnailUrl,
            ThumbnailHoverKind.None,
            null,
            [],
            [],
            entity.RatingValue,
            entity.IsFavorite ?? false,
            entity.IsNsfw ?? false,
            entity.IsOrganized ?? false) {
            CoverThumb2xUrl = artwork.Thumbnail2xUrl ?? artwork.ThumbnailUrl,
            IsWanted = entity.IsWanted ?? false,
            Progress = ResolveThumbnailProgress(entity)
        };
    }

    /// <summary>
    /// Computes the 0..1 progress meter fraction for a nested thumbnail from the hydrated
    /// entity's playback and reading-progress capabilities, mirroring the row-based browse
    /// projection so detail-page child grids match library grids.
    /// </summary>
    private static double? ResolveThumbnailProgress(Entity entity) {
        if (entity.Playback is { } playback) {
            if (playback.CompletedAt is not null) {
                return 1.0;
            }

            var duration = entity.Technical?.Duration;
            if (playback.ResumeTime > TimeSpan.Zero && duration is { } total && total > TimeSpan.Zero) {
                return Math.Clamp(playback.ResumeTime.TotalSeconds / total.TotalSeconds, 0, 1);
            }

            return null;
        }

        if (entity.Progress is { } progress) {
            if (progress.CompletedAt is not null) {
                return 1.0;
            }

            if (progress.Total > 0 && progress.Index > 0) {
                return Math.Clamp((double)progress.Index / progress.Total, 0, 1);
            }
        }

        return null;
    }

}
