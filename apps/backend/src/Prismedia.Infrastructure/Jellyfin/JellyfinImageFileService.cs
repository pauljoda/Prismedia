using Prismedia.Application.Jellyfin;
using Prismedia.Infrastructure.Media.Processing;

namespace Prismedia.Infrastructure.Jellyfin;

/// <summary>Resolves Jellyfin image asset URLs against Prismedia's generated asset cache.</summary>
public sealed class JellyfinImageFileService : IJellyfinImageFileService {
    private const string PrismediaLogoImagePath = "/brand/prismedia-logo.png";

    private readonly AssetPathService _assets;

    public JellyfinImageFileService(AssetPathService assets) {
        _assets = assets;
    }

    /// <inheritdoc />
    public Task<JellyfinImageFile?> ResolveAsync(JellyfinImageAsset asset, CancellationToken cancellationToken) {
        if (asset.Path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            asset.Path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            return Task.FromResult<JellyfinImageFile?>(new JellyfinImageFile(
                null,
                asset.Path,
                asset.ContentType,
                asset.ImageTag));
        }

        if (asset.Path.Equals(PrismediaLogoImagePath, StringComparison.Ordinal)) {
            return Task.FromResult<JellyfinImageFile?>(new JellyfinImageFile(
                null,
                asset.Path,
                asset.ContentType,
                asset.ImageTag));
        }

        var diskPath = asset.Path.StartsWith("/assets/", StringComparison.Ordinal)
            ? _assets.ResolveAssetDiskPath(asset.Path)
            : asset.Path;
        if (diskPath is null || !File.Exists(diskPath)) {
            return Task.FromResult<JellyfinImageFile?>(null);
        }

        return Task.FromResult<JellyfinImageFile?>(new JellyfinImageFile(
            diskPath,
            null,
            asset.ContentType,
            asset.ImageTag));
    }
}
