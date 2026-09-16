using Microsoft.EntityFrameworkCore;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Files;

/// <summary>Checks durable local boundaries while the caller holds the library configuration lease.</summary>
internal static class ExternalLibraryBoundaryPaths {
    internal static async Task<bool> OverlapsAsync(PrismediaDbContext db, string? path, CancellationToken token) {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        var candidate = CompletedPayloadFileSystem.CanonicalPath(Path.GetFullPath(path));
        var boundaries = await db.ExternalLibraryMounts.Select(mount => mount.LocalPath).ToArrayAsync(token);
        return boundaries.Any(boundary => CompletedPayloadFileSystem.Overlaps(candidate, CompletedPayloadFileSystem.CanonicalPath(boundary)));
    }
}
