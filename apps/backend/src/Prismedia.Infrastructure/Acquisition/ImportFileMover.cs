using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Places imported files on disk. A move is a cheap rename when the download dir and library root
/// share a filesystem and transparently falls back to copy+delete across volumes; hardlink mode links
/// the file (instant, no space, the download keeps seeding) and falls back to copy across volumes.
/// Ordinary placements give colliding targets a stable numeric suffix; checkpoint-driven placements
/// can instead require the pre-resolved exact path and fail safely if it is no longer available.
/// </summary>
public sealed class ImportFileMover : IImportFileMover {
    /// <inheritdoc />
    public string ResolveExactTargetPath(string desiredTargetPath, IReadOnlyCollection<string> reservedTargetPaths) {
        var reserved = reservedTargetPaths
            .Select(Path.GetFullPath)
            .ToHashSet(FileSystemPathComparison.Comparer);
        return ResolveCollision(desiredTargetPath, reserved);
    }

    /// <inheritdoc />
    public Task<string> PlaceAsync(ResolvedImportItem item, ImportMode mode, CancellationToken cancellationToken) =>
        PlaceAtAsync(item, mode, resolveCollision: true, cancellationToken);

    /// <inheritdoc />
    public Task<string> PlaceExactAsync(ResolvedImportItem item, ImportMode mode, CancellationToken cancellationToken) =>
        PlaceAtAsync(item, mode, resolveCollision: false, cancellationToken);

    private async Task<string> PlaceAtAsync(
        ResolvedImportItem item,
        ImportMode mode,
        bool resolveCollision,
        CancellationToken cancellationToken) {
        var directory = Path.GetDirectoryName(item.TargetAbsolutePath);
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }

        var target = resolveCollision
            ? ResolveExactTargetPath(item.TargetAbsolutePath, Array.Empty<string>())
            : item.TargetAbsolutePath;
        if (mode == ImportMode.Copy) {
            await CopyAndPublishAsync(item.SourceAbsolutePath, target, cancellationToken);
            return target;
        }

        if (mode == ImportMode.Hardlink) {
            // A hardlink only works within one filesystem; across volumes the copy fallback preserves
            // the same observable behavior (source stays seeding, target lands in the library).
            if (!HardLink.TryCreate(item.SourceAbsolutePath, target)) {
                await CopyAndPublishAsync(item.SourceAbsolutePath, target, cancellationToken);
            }

            return target;
        }

        try {
            File.Move(item.SourceAbsolutePath, target);
        } catch (IOException) {
            // Cross-device move: stage a complete copy beside the target, atomically publish it, then
            // delete the source. A cancellation or failed copy can never expose a truncated media file
            // at the durable checkpoint path.
            await CopyAndPublishAsync(item.SourceAbsolutePath, target, cancellationToken);
            File.Delete(item.SourceAbsolutePath);
        }

        return target;
    }

    private static async Task CopyAndPublishAsync(
        string source,
        string target,
        CancellationToken cancellationToken) {
        var directory = Path.GetDirectoryName(target) ?? string.Empty;
        var staged = Path.Combine(
            directory,
            $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.prismedia-import");

        try {
            await CopyAsync(source, staged, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Same-directory rename publishes the complete file atomically and never overwrites a path
            // another import claimed after collision resolution.
            File.Move(staged, target);
        } finally {
            TryDelete(staged);
        }
    }

    private static async Task CopyAsync(string source, string target, CancellationToken cancellationToken) {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static void TryDelete(string path) {
        try {
            File.Delete(path);
        } catch {
            // Best-effort cleanup. The staging extension is intentionally not discoverable media.
        }
    }

    private static string ResolveCollision(string target, IReadOnlySet<string> reservedTargetPaths) {
        if (IsAvailable(target, reservedTargetPaths)) {
            return target;
        }

        var directory = Path.GetDirectoryName(target) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);
        for (var index = 2; ; index++) {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (IsAvailable(candidate, reservedTargetPaths)) {
                return candidate;
            }
        }
    }

    private static bool IsAvailable(string target, IReadOnlySet<string> reservedTargetPaths) =>
        !File.Exists(target)
        && !Directory.Exists(target)
        && !reservedTargetPaths.Contains(Path.GetFullPath(target));
}
