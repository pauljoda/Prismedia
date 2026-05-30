using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Shared cover-selection priority used by both the thumbnail read projection and
/// grid-thumbnail generation. Keeping the rule in one place guarantees the small
/// grid variant is always derived from the exact same cover the API reports, so
/// the two never drift apart in a responsive <c>srcset</c>.
/// </summary>
internal static class EntityCoverSelection {
    /// <summary>File roles that can act as an entity cover, highest-intent first.</summary>
    public static readonly EntityFileRole[] CoverRoles =
    [
        EntityFileRole.Thumbnail,
        EntityFileRole.Poster,
        EntityFileRole.Cover,
        EntityFileRole.Logo,
        EntityFileRole.Backdrop
    ];

    /// <summary>Picks the winning cover from a set of cover-eligible files, or null when empty.</summary>
    public static EntityFileRow? Select(IEnumerable<EntityFileRow> coverFiles) =>
        coverFiles
            .OrderBy(file => SourcePriority(file.Role, file.Source, file.Path))
            .ThenBy(file => RolePriority(file.Role))
            .ThenBy(file => file.CreatedAt)
            .FirstOrDefault();

    private static int RolePriority(EntityFileRole role) =>
        role switch {
            EntityFileRole.Thumbnail => 0,
            EntityFileRole.Poster => 1,
            EntityFileRole.Cover => 2,
            EntityFileRole.Logo => 3,
            _ => 4
        };

    private static int SourcePriority(EntityFileRole role, string? source, string path) {
        if (role == EntityFileRole.Backdrop) {
            return 2;
        }

        return source == "custom" ||
            path.Contains("/custom/artwork/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/plugins/artwork/", StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1;
    }
}
