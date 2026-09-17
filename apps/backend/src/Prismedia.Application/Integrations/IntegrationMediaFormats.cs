using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Exact executor representations accepted by the host's verified import pipeline.</summary>
public static class IntegrationMediaFormats {
    /// <summary>Maximum compressed bytes for a standalone image.</summary>
    public const long MaximumImageBytes = 64L * 1024 * 1024;
    /// <summary>Whether the suggested extension is supported for this exact media kind.</summary>
    public static bool IsSupported(EntityKind kind, string fileName) => kind switch {
        EntityKind.Book => Path.GetExtension(fileName).ToLowerInvariant() is ".epub" or ".pdf",
        EntityKind.ComicInstallment => Path.GetExtension(fileName).Equals(".cbz", StringComparison.OrdinalIgnoreCase),
        EntityKind.Image => Path.GetExtension(fileName).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp",
        _ => false
    };
    /// <summary>Whether a declared source media type can become an exact supported import for this media kind.</summary>
    public static bool IsSupportedMediaType(EntityKind kind, string mediaType) {
        var normalized = mediaType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return kind switch {
            EntityKind.Book => normalized is "application/epub+zip" or "application/pdf",
            EntityKind.ComicInstallment => normalized is "application/vnd.comicbook+zip" or "application/zip",
            EntityKind.Image => normalized is "image/jpeg" or "image/png" or "image/webp",
            _ => false
        };
    }
    /// <summary>Rechecks the destination's enabled scanner before acceptance and before placement.</summary>
    public static bool SupportsRoot(EntityKind kind, LibraryRootData root) => root.Enabled && !root.IsReadOnly && kind switch {
        EntityKind.Book or EntityKind.ComicInstallment => root.ScanBooks,
        EntityKind.Image => root.ScanImages,
        EntityKind.Gallery => root.ScanImages && root.Recursive,
        _ => false
    };
}
