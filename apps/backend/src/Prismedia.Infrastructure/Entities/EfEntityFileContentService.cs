using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// EF-backed resolver for files attached to entities.
/// </summary>
public sealed class EfEntityFileContentService(PrismediaDbContext db) : IEntityFileContentService {
    /// <inheritdoc />
    public async Task<EntityFileContent?> GetContentAsync(
        Guid entityId,
        string role,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(role) || !role.TryDecodeAs<EntityFileRole>(out var decodedRole)) {
            return null;
        }

        var file = await db.EntityFiles.AsNoTracking()
            .Where(row => row.EntityId == entityId && row.Role == decodedRole)
            .Select(row => new {
                row.EntityId,
                row.Role,
                row.Path,
                row.MimeType,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (file is null) {
            return null;
        }

        var contentType = string.IsNullOrWhiteSpace(file.MimeType)
            ? MimeForExtension(Path.GetExtension(file.Path))
            : file.MimeType;

        return new EntityFileContent(
            file.EntityId,
            file.Role.ToCode(),
            file.Path,
            contentType);
    }

    private static string MimeForExtension(string extension) =>
        extension.ToLowerInvariant() switch {
            ".jpg" or ".jpeg" => MediaContentTypes.ImageJpeg,
            ".png" => MediaContentTypes.ImagePng,
            ".gif" => MediaContentTypes.ImageGif,
            ".webp" => MediaContentTypes.ImageWebp,
            ".avif" => MediaContentTypes.ImageAvif,
            ".mp4" or ".m4v" => MediaContentTypes.VideoMp4,
            ".webm" => MediaContentTypes.VideoWebm,
            ".ogg" or ".ogv" => MediaContentTypes.VideoOgg,
            ".mov" => MediaContentTypes.VideoQuicktime,
            ".mkv" => MediaContentTypes.VideoMatroska,
            ".avi" => MediaContentTypes.VideoAvi,
            ".wmv" => MediaContentTypes.VideoWmv,
            ".flv" => MediaContentTypes.VideoFlv,
            ".ts" or ".m2ts" => MediaContentTypes.VideoMp2t,
            _ => MediaContentTypes.OctetStream,
        };
}
