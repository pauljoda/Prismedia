using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Default <see cref="IOwnedFileReplacer"/>: an in-place, same-path swap of an owned book file for a
/// strictly-better one. The owned file is renamed aside to a <c>.prismedia-bak</c> (same directory → an
/// atomic rename, and the previous file is kept so it is always recoverable; the scanner ignores it because
/// the extension is not importable), then the new file is moved into the owned file's exact path.
/// <para>
/// v1 deliberately handles only same-extension upgrades (e.g. a web EPUB → a retail EPUB): installing at the
/// exact owned path keeps the library's file row and the reader's progress valid with no entity surgery. A
/// format change (different extension) would orphan the entity, so it is refused here and surfaced for manual
/// handling. Any failure restores the original from the backup, so the owned file is never lost.
/// </para>
/// </summary>
public sealed class OwnedFileReplacer(ILogger<OwnedFileReplacer> logger) : IOwnedFileReplacer {
    private const string BackupSuffix = ".prismedia-bak";
    private const string StagedSuffix = ".prismedia-new";

    public Task<OwnedFileReplaceResult> ReplaceAsync(string ownedFolder, string newContentPath, BookFormatTier ownedFormatTier, CancellationToken cancellationToken) {
        var owned = FindBookFile(ownedFolder);
        if (owned is null) {
            return Task.FromResult(OwnedFileReplaceResult.Failed("Could not find a single owned book file to replace."));
        }

        var incoming = FindBookFile(newContentPath);
        if (incoming is null) {
            return Task.FromResult(OwnedFileReplaceResult.Failed("The upgrade download has no single importable book file."));
        }

        var incomingInfo = new FileInfo(incoming);
        if (!incomingInfo.Exists || incomingInfo.Length == 0) {
            return Task.FromResult(OwnedFileReplaceResult.Failed("The upgrade file is missing or empty."));
        }

        var ownedExtension = Path.GetExtension(owned);
        var incomingExtension = Path.GetExtension(incoming);
        if (!string.Equals(ownedExtension, incomingExtension, StringComparison.OrdinalIgnoreCase)) {
            // A format change moves the file to a different extension/path, which would orphan the library
            // entity and the reader's progress. Refuse it here; the caller surfaces it for manual replacement.
            return Task.FromResult(OwnedFileReplaceResult.Failed($"Upgrading the format ({ownedExtension} → {incomingExtension}) needs a manual replacement."));
        }

        // Trust the actual extension, not free text in the path (a folder named "(epub)" must not make a PDF
        // look reflowable). Same-extension is already enforced above, so this equals the owned tier.
        var newFormat = BookFormatDetection.FormatTierFromExtension(incoming);
        if (newFormat < ownedFormatTier) {
            return Task.FromResult(OwnedFileReplaceResult.Failed("The upgrade file's format is lower than the owned file's."));
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
            return Task.FromResult(OwnedFileReplaceResult.Failed($"Could not stage the upgrade: {ex.Message}"));
        }

        try {
            File.Move(staged, owned, overwrite: true); // atomic same-directory replace; the owned path is never empty
            var installed = new FileInfo(owned);
            if (!installed.Exists || installed.Length == 0) {
                throw new IOException("The installed file is missing or empty after the move.");
            }

            return Task.FromResult(OwnedFileReplaceResult.Ok(owned, newFormat));
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
            return Task.FromResult(OwnedFileReplaceResult.Failed($"The swap failed and the original was kept: {ex.Message}"));
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

    /// <summary>Returns the single importable book file at or under a path, or null when there is none or more than one (ambiguous).</summary>
    private static string? FindBookFile(string path) {
        if (File.Exists(path)) {
            return ImportPlanBuilder.SupportedExtensions.Contains(Path.GetExtension(path)) ? path : null;
        }

        if (!Directory.Exists(path)) {
            return null;
        }

        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(file => ImportPlanBuilder.SupportedExtensions.Contains(Path.GetExtension(file)))
            .Take(2)
            .ToArray();
        return files.Length == 1 ? files[0] : null;
    }
}
