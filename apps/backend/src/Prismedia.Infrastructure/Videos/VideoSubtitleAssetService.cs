using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Media;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// EF-backed subtitle asset resolver for video subtitle tracks.
/// </summary>
public sealed class VideoSubtitleAssetService : IVideoSubtitleAssetService {
    private readonly PrismediaDbContext _db;

    /// <summary>
    /// Creates a subtitle asset resolver over the database context.
    /// </summary>
    /// <param name="db">Database context used to find subtitle rows.</param>
    public VideoSubtitleAssetService(PrismediaDbContext db) {
        _db = db;
    }

    /// <summary>
    /// Finds the normalized WebVTT subtitle file for one subtitle track.
    /// </summary>
    /// <param name="videoId">Video entity identifier that owns the track.</param>
    /// <param name="trackId">Subtitle track identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the lookup.</param>
    /// <returns>Subtitle asset metadata, or null when the track or file is unavailable.</returns>
    public async Task<VideoSubtitleAsset?> GetSubtitleAsync(
        Guid videoId,
        Guid trackId,
        CancellationToken cancellationToken) {
        var path = await _db.EntitySubtitles
            .AsNoTracking()
            .Where(row => row.EntityId == videoId && row.Id == trackId)
            .Select(row => row.StoragePath)
            .SingleOrDefaultAsync(cancellationToken);

        return ExistingAsset(path, MediaContentTypes.VttUtf8);
    }

    /// <summary>
    /// Finds the original ASS/SSA subtitle source for one subtitle track, when preserved.
    /// </summary>
    /// <param name="videoId">Video entity identifier that owns the track.</param>
    /// <param name="trackId">Subtitle track identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the lookup.</param>
    /// <returns>Subtitle source metadata, or null when no raw source is available.</returns>
    public async Task<VideoSubtitleAsset?> GetSubtitleSourceAsync(
        Guid videoId,
        Guid trackId,
        CancellationToken cancellationToken) {
        var row = await _db.EntitySubtitles
            .AsNoTracking()
            .Where(subtitle => subtitle.EntityId == videoId && subtitle.Id == trackId)
            .Select(subtitle => new {
                subtitle.SourcePath,
                subtitle.SourceFormat
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null ||
            string.IsNullOrWhiteSpace(row.SourcePath) ||
            !IsStyledSubtitleFormat(row.SourceFormat)) {
            return null;
        }

        return ExistingAsset(row.SourcePath, MediaContentTypes.SsaUtf8);
    }

    private static VideoSubtitleAsset? ExistingAsset(string? path, string contentType) {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path)) {
            return null;
        }

        return new VideoSubtitleAsset(path, contentType);
    }

    private static bool IsStyledSubtitleFormat(string? format) =>
        string.Equals(format, "ass", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(format, "ssa", StringComparison.OrdinalIgnoreCase);
}
