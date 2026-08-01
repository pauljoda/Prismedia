using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using ContractEntityImageAsset = Prismedia.Contracts.Entities.EntityImageAsset;

namespace Prismedia.Application.Entities;

/// <summary>
/// Projects the canonical artwork ordering and URLs shared by entity documents and
/// nested thumbnails. Keeping the ordering in one place prevents the same entity from
/// selecting different artwork on detail and grid surfaces.
/// </summary>
internal static class EntityArtworkProjection {
    private static readonly EntityFileRole[] SupportedManualImageRoles =
    [
        EntityFileRole.Thumbnail,
        EntityFileRole.Poster,
        EntityFileRole.Backdrop,
        EntityFileRole.Cover,
        EntityFileRole.Logo
    ];

    /// <summary>Projects the entity's ordered artwork and derived thumbnail URLs.</summary>
    internal static ImagesCapability Project(Entity entity) {
        var assets = OrderedArtworkFiles(entity)
            .Select(file => new ContractEntityImageAsset(file.Role, file.Path, file.MimeType))
            .ToArray();
        var gridThumbnail = entity.EntityFiles
            .FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail)?.Path;
        var thumbnailUrl = gridThumbnail ?? assets.FirstOrDefault()?.Path;
        var thumbnail2xUrl = entity.EntityFiles
            .FirstOrDefault(file => file.Role == EntityFileRole.GridThumbnail2x)?.Path;

        return new ImagesCapability(
            SupportedManualImageRoles,
            assets,
            thumbnailUrl,
            thumbnail2xUrl,
            assets.FirstOrDefault()?.Path);
    }

    private static IEnumerable<Prismedia.Domain.Entities.EntityFile> OrderedArtworkFiles(Entity entity) =>
        entity.EntityFiles
            .Where(file => file.Role is EntityFileRole.Thumbnail or EntityFileRole.Poster
                or EntityFileRole.Cover or EntityFileRole.Backdrop or EntityFileRole.Logo)
            .OrderBy(file => ImageSourcePriority(file.Role, file.Path))
            .ThenBy(file => file.Role switch {
                EntityFileRole.Thumbnail => 0,
                EntityFileRole.Poster => 1,
                EntityFileRole.Cover => 2,
                EntityFileRole.Logo => 3,
                _ => 4
            });

    private static bool IsCustomPath(string path) =>
        path.Contains("/custom/artwork/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/plugins/artwork/", StringComparison.OrdinalIgnoreCase);

    private static int ImageSourcePriority(EntityFileRole role, string path) =>
        role == EntityFileRole.Backdrop ? 2 : IsCustomPath(path) ? 0 : 1;
}
