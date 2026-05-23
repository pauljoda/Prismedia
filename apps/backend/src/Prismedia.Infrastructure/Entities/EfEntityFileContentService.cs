using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
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
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".ogg" or ".ogv" => "video/ogg",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            ".wmv" => "video/x-ms-wmv",
            ".flv" => "video/x-flv",
            ".ts" or ".m2ts" => "video/mp2t",
            _ => "application/octet-stream",
        };
}
