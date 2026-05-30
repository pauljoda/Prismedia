namespace Prismedia.Application.Jellyfin;

/// <summary>Application port for resolving Jellyfin image assets to streamable files or redirects.</summary>
public interface IJellyfinImageFileService {
    /// <summary>Resolves an image asset to a local file or redirect target.</summary>
    Task<JellyfinImageFile?> ResolveAsync(JellyfinImageAsset asset, CancellationToken cancellationToken);
}

/// <summary>Resolved image endpoint payload.</summary>
public sealed record JellyfinImageFile(
    string? FilePath,
    string? RedirectUrl,
    string ContentType,
    string ImageTag);
