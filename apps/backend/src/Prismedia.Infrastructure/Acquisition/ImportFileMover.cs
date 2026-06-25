using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Places imported book files on disk. A move is a cheap rename when the download dir and library root
/// share a filesystem and transparently falls back to copy+delete across volumes. Colliding targets get
/// a stable numeric suffix so an import never overwrites existing library files.
/// </summary>
public sealed class ImportFileMover : IImportFileMover {
    public async Task<string> PlaceAsync(ResolvedImportItem item, ImportMode mode, CancellationToken cancellationToken) {
        var directory = Path.GetDirectoryName(item.TargetAbsolutePath);
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }

        var target = ResolveCollision(item.TargetAbsolutePath);
        if (mode == ImportMode.Copy) {
            await CopyAsync(item.SourceAbsolutePath, target, cancellationToken);
            return target;
        }

        try {
            File.Move(item.SourceAbsolutePath, target);
        } catch (IOException) {
            // Cross-device move: fall back to copy then delete the source.
            await CopyAsync(item.SourceAbsolutePath, target, cancellationToken);
            File.Delete(item.SourceAbsolutePath);
        }

        return target;
    }

    private static async Task CopyAsync(string source, string target, CancellationToken cancellationToken) {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static string ResolveCollision(string target) {
        if (!File.Exists(target)) {
            return target;
        }

        var directory = Path.GetDirectoryName(target) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);
        for (var index = 2; ; index++) {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate)) {
                return candidate;
            }
        }
    }
}
