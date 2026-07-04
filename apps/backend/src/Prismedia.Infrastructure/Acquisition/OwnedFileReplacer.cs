using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Default <see cref="IOwnedFileReplacer"/>: an in-place, same-path swap of an owned single-file payload for
/// a strictly-better one. The owned file is renamed aside to a <c>.prismedia-bak</c> (same directory → an
/// atomic rename, and the previous file is kept so it is always recoverable; the scanner ignores it because
/// the extension is not importable), then the new file is moved into the owned file's exact path.
/// <para>
/// It deliberately handles only same-extension upgrades (a web EPUB → a retail EPUB; an mkv → a better mkv):
/// installing at the exact owned path keeps the library's file row and playback/reader progress valid with no
/// entity surgery. A format change (different extension — e.g. mkv → mp4) would orphan the entity, so it is
/// refused here and surfaced for manual handling. The kind selects which single file to find (a book file, or
/// a video file for a movie/single episode) and whether the book format-tier guard applies. Any failure
/// restores the original from the backup, so the owned file is never lost.
/// </para>
/// </summary>
public sealed class OwnedFileReplacer(IRecycleBin recycleBin, ILogger<OwnedFileReplacer> logger) : IOwnedFileReplacer {
    private const string BackupSuffix = ".prismedia-bak";
    private const string StagedSuffix = ".prismedia-new";

    public async Task<OwnedFileReplaceResult> ReplaceAsync(string ownedFolder, string newContentPath, BookFormatTier ownedFormatTier, CancellationToken cancellationToken, EntityKind kind = EntityKind.Book) {
        var isVideo = MediaQualityLadder.IsUpgradeCapableKind(kind);
        var extensions = isVideo ? MovieImportPlanBuilder.VideoExtensions : ImportPlanBuilder.SupportedExtensions;
        var fileNoun = isVideo ? "video" : "book";

        var owned = FindSingleFile(ownedFolder, extensions);
        if (owned is null) {
            return (OwnedFileReplaceResult.Failed($"Could not find a single owned {fileNoun} file to replace."));
        }

        var incoming = FindSingleFile(newContentPath, extensions);
        if (incoming is null) {
            return (OwnedFileReplaceResult.Failed($"The upgrade download has no single importable {fileNoun} file."));
        }

        var incomingInfo = new FileInfo(incoming);
        if (!incomingInfo.Exists || incomingInfo.Length == 0) {
            return (OwnedFileReplaceResult.Failed("The upgrade file is missing or empty."));
        }

        var ownedExtension = Path.GetExtension(owned);
        var incomingExtension = Path.GetExtension(incoming);
        if (!string.Equals(ownedExtension, incomingExtension, StringComparison.OrdinalIgnoreCase)) {
            // A format change moves the file to a different extension/path, which would orphan the library
            // entity and its playback/reader progress. Refuse it here; the caller surfaces it for manual
            // replacement. This applies to video (mkv → mp4) exactly as it does to books, for the same reason.
            return (OwnedFileReplaceResult.Failed($"Upgrading the format ({ownedExtension} → {incomingExtension}) needs a manual replacement."));
        }

        // Books enforce a format-tier floor from the actual extension (a folder named "(epub)" must not make a
        // PDF look reflowable). Video has no extension-derived format tier — same-extension is already enforced
        // above and the video quality upgrade is judged from the release title by the caller — so it is a
        // pass-through here (NewFormat stays Unknown; the caller records the media-quality code).
        var newFormat = isVideo ? BookFormatTier.Unknown : BookFormatDetection.FormatTierFromExtension(incoming);
        if (!isVideo && newFormat < ownedFormatTier) {
            return (OwnedFileReplaceResult.Failed("The upgrade file's format is lower than the owned file's."));
        }

        var backup = owned + BackupSuffix;
        var staged = owned + StagedSuffix;
        try {
            // Stage the new file beside the owned file FIRST (this is the only possibly-cross-device move, and
            // it never touches the owned file), then preserve the original as a backup COPY, then atomically
            // replace the owned file in a single rename. Ordering it this way means the owned path is never
            // momentarily empty — a concurrent scan always sees either the old or the new file, never neither.
            if (File.Exists(staged)) {
                File.Delete(staged); // clear a stale staged file from a prior aborted swap
            }

            File.Move(incoming, staged);
            File.Copy(owned, backup, overwrite: true); // keep the previous file as a recoverable backup
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "OwnedFileReplacer: could not stage the upgrade for {Path}.", owned);
            TryDelete(staged);
            return (OwnedFileReplaceResult.Failed($"Could not stage the upgrade: {ex.Message}"));
        }

        try {
            File.Move(staged, owned, overwrite: true); // atomic same-directory replace; the owned path is never empty
            var installed = new FileInfo(owned);
            if (!installed.Exists || installed.Length == 0) {
                throw new IOException("The installed file is missing or empty after the move.");
            }

            // With a recycle bin configured the previous file moves there (purged after the cleanup window);
            // otherwise it stays beside the new one as the recoverable .prismedia-bak sidecar.
            if (await recycleBin.TryMoveToBinAsync(backup, cancellationToken) is { } binned) {
                logger.LogDebug("OwnedFileReplacer: previous file recycled to {Binned}.", binned);
            }

            return OwnedFileReplaceResult.Ok(owned, newFormat);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "OwnedFileReplacer: swap failed for {Path}; the original is intact (or restorable from backup).", owned);
            // The atomic replace either fully succeeded or left the owned file as it was; if it somehow left the
            // owned file missing, restore it from the backup copy. The staged file is discarded.
            try {
                if (!File.Exists(owned) && File.Exists(backup)) {
                    File.Copy(backup, owned, overwrite: false);
                }
            } catch (Exception restoreEx) when (restoreEx is not OperationCanceledException) {
                logger.LogError(restoreEx, "OwnedFileReplacer: failed to restore the backup at {Backup}; it remains on disk.", backup);
            }

            TryDelete(staged);
            return (OwnedFileReplaceResult.Failed($"The swap failed and the original was kept: {ex.Message}"));
        }
    }

    private static void TryDelete(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch {
            // best-effort cleanup of a staged temp file
        }
    }

    /// <summary>
    /// Returns the single file at or under a path whose extension is in <paramref name="extensions"/>, or
    /// null when there is none or more than one (ambiguous). Shared by the book and video replace paths;
    /// only the extension set differs between kinds.
    /// </summary>
    private static string? FindSingleFile(string path, IReadOnlySet<string> extensions) {
        if (File.Exists(path)) {
            return extensions.Contains(Path.GetExtension(path)) ? path : null;
        }

        if (!Directory.Exists(path)) {
            return null;
        }

        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(file => extensions.Contains(Path.GetExtension(file)))
            .Take(2)
            .ToArray();
        return files.Length == 1 ? files[0] : null;
    }
}
