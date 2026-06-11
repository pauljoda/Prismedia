using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Single source for interpreting a provider image's <see cref="MediaImageKind"/> tag: which
/// <see cref="EntityFileRole"/> a downloaded image is stored under, and which candidate to pick by
/// kind preference. Replaces the per-site bare image-kind string forks across the apply/artwork paths.
/// </summary>
public static class ImageKindRoleResolver {
    /// <summary>True when the (wire-string) image kind matches the given <see cref="MediaImageKind"/>.</summary>
    public static bool Is(string? imageKind, MediaImageKind kind) =>
        string.Equals(imageKind, kind.ToCode(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The first image whose kind matches one of <paramref name="kinds"/>, in preference order, or null.
    /// </summary>
    public static ImageCandidate? Pick(IReadOnlyList<ImageCandidate> images, params MediaImageKind[] kinds) {
        foreach (var kind in kinds) {
            var match = images.FirstOrDefault(image => Is(image.Kind, kind));
            if (match is not null) {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// Maps a provider image-kind tag to the persisted <see cref="EntityFileRole"/>. Unknown or
    /// non-role kinds (still, banner, hero, profile, thumbnail) store as <see cref="EntityFileRole.Thumbnail"/>.
    /// </summary>
    public static EntityFileRole RoleFor(string? imageKind) =>
        imageKind.TryDecodeAs<MediaImageKind>(out var kind) ? RoleFor(kind) : EntityFileRole.Thumbnail;

    /// <summary>Maps a <see cref="MediaImageKind"/> to the persisted <see cref="EntityFileRole"/>.</summary>
    public static EntityFileRole RoleFor(MediaImageKind kind) => kind switch {
        MediaImageKind.Poster => EntityFileRole.Poster,
        MediaImageKind.Cover => EntityFileRole.Cover,
        MediaImageKind.Backdrop => EntityFileRole.Backdrop,
        MediaImageKind.Logo => EntityFileRole.Logo,
        _ => EntityFileRole.Thumbnail
    };
}
