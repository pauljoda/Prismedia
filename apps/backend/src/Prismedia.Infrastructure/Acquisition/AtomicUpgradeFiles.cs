using System.Security.Cryptography;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Uses captured file facts and attempt-specific byte evidence to recover atomic replacements, including approved video container changes.</summary>
public sealed class AtomicUpgradeFiles(IOwnedFileReplacer replacer, IRecycleBin recycleBin) : IAtomicUpgradeFiles {
    /// <inheritdoc />
    public Task<AtomicUpgradeFilePlan> PrepareAsync(UpgradeReplaceTarget target, CancellationToken cancellationToken, bool allowFormatChange = false) {
        cancellationToken.ThrowIfCancellationRequested();
        var video = MediaQualityLadder.IsUpgradeCapableKind(target.ParentKind);
        var owned = Select(target.ParentFinalSourcePath, video);
        var incoming = Select(target.ChildContentPath, video);
        if (owned is null || incoming is null || FileSystemPathComparison.Equals(owned, incoming))
            throw new IOException("The owned and downloaded payloads must each contain one distinct importable file.");
        if (!string.Equals(Path.GetExtension(owned), Path.GetExtension(incoming), StringComparison.OrdinalIgnoreCase) && !(video && allowFormatChange))
            throw new IOException("Upgrading to a different file format needs a manual replacement.");
        if (File.Exists(OwnedFileReplacementArtifacts.StagedPath(owned)) || Directory.Exists(OwnedFileReplacementArtifacts.StagedPath(owned)))
            throw new IOException("A previous replacement is still staged beside the owned file; review that attempt first.");
        var plan = new AtomicUpgradeFilePlan(owned, incoming, Snapshot(owned), Snapshot(incoming),
            video ? BookFormatTier.Unknown : BookFormatDetection.FormatTierFromExtension(incoming), video && allowFormatChange);
        if (DifferentDestinationOccupied(plan))
            throw new IOException("The replacement destination already exists; both files were retained for review.");
        if (!video && plan.IncomingFormat < target.ParentOwnedQuality.Format)
            throw new IOException("The upgrade file's format is lower than the owned file's.");
        return Task.FromResult(plan);
    }

    /// <inheritdoc />
    public async Task<AtomicUpgradeFileRecovery> RecoverAsync(AtomicUpgradeCheckpoint checkpoint, CancellationToken cancellationToken) {
        var files = checkpoint.Files;
        var staged = OwnedFileReplacementArtifacts.StagedPath(files.OwnedPath);
        var evidence = checkpoint.EvidencePath;
        var backup = checkpoint.BackupPath;
        if (Directory.Exists(staged) || Directory.Exists(evidence) || Directory.Exists(backup)) return Hold();
        if (File.Exists(evidence) && Matches(evidence, files.Incoming)
            && Matches(backup, files.Owned)
            && !File.Exists(staged) && !File.Exists(files.IncomingPath)
            && await SameBytesAsync(evidence, files.InstallPath, cancellationToken)) {
            if (!FileSystemPathComparison.Equals(files.OwnedPath, files.InstallPath)) {
                // A crash can occur between installing the new extension and retiring the old file.
                // Retire only the exact original proven by this attempt's retained backup.
                if (Directory.Exists(files.OwnedPath)) return Hold();
                if (File.Exists(files.OwnedPath)) {
                    if (!Matches(files.OwnedPath, files.Owned)
                        || !await SameBytesAsync(backup, files.OwnedPath, cancellationToken)) return Hold();
                    File.Delete(files.OwnedPath);
                }
            }
            return new(OwnedFileReplaceResult.Ok(files.InstallPath, files.IncomingFormat));
        }
        if (!Matches(files.OwnedPath, files.Owned) || DifferentDestinationOccupied(files)) return Hold();
        if (File.Exists(staged)) {
            if (!Matches(staged, files.Incoming)
                || !await SameBytesAsync(staged, evidence, cancellationToken)) return Hold();
            if (File.Exists(files.IncomingPath)) {
                if (!Matches(files.IncomingPath, files.Incoming)
                    || !await SameBytesAsync(staged, files.IncomingPath, cancellationToken)) return Hold();
                File.Delete(staged);
            } else {
                File.Move(staged, files.IncomingPath, overwrite: false);
            }
        }
        if (!Matches(files.IncomingPath, files.Incoming)) return Hold();
        if (File.Exists(evidence) && !await SameBytesAsync(files.IncomingPath, evidence, cancellationToken)) return Hold();
        if (File.Exists(backup) && !await SameBytesAsync(files.OwnedPath, backup, cancellationToken)) return Hold();
        return new();
    }

    /// <inheritdoc />
    public Task<OwnedFileReplaceResult> ReplaceAsync(AtomicUpgradeCheckpoint checkpoint, CancellationToken cancellationToken) {
        if (DifferentDestinationOccupied(checkpoint.Files) || !Matches(checkpoint.Files.OwnedPath, checkpoint.Files.Owned)
            || !Matches(checkpoint.Files.IncomingPath, checkpoint.Files.Incoming))
            return Task.FromResult(OwnedFileReplaceResult.Failed("The prepared files changed before replacement; recovery evidence was retained."));
        return replacer.ReplaceRetainingBackupAsync(checkpoint.Files.OwnedPath, checkpoint.Files.IncomingPath,
            checkpoint.Files.IncomingFormat, cancellationToken, checkpoint.Kind,
            allowFormatChange: checkpoint.Files.AllowFormatChange, recoveryBackupPath: checkpoint.BackupPath, incomingEvidencePath: checkpoint.EvidencePath);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(AtomicUpgradeCheckpoint checkpoint, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(checkpoint.EvidencePath);
        if (!File.Exists(checkpoint.BackupPath)
            || await recycleBin.TryMoveToBinAsync(checkpoint.BackupPath, cancellationToken) is not null) return;
        // Restore the ordinary one-backup policy after commit. If another installation has already
        // changed the owned file, retain this attempt's original rather than replacing its newer backup.
        if (Matches(checkpoint.Files.InstallPath, checkpoint.Files.Incoming)) {
            File.Move(checkpoint.BackupPath, OwnedFileReplacementArtifacts.BackupPath(checkpoint.Files.OwnedPath), overwrite: true);
        }
    }

    private static bool DifferentDestinationOccupied(AtomicUpgradeFilePlan files) =>
        !FileSystemPathComparison.Equals(files.OwnedPath, files.InstallPath)
        && (File.Exists(files.InstallPath) || Directory.Exists(files.InstallPath));

    private static AtomicUpgradeFileRecovery Hold() => new(HoldReason:
        "The interrupted replacement cannot be proven from its saved file evidence. Both the original and replacement artifacts were retained for review.");

    private static string? Select(string? path, bool video) {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (video) return VideoUpgradeFileSelection.Find(path) is { } selected ? Path.GetFullPath(selected) : null;
        if (File.Exists(path)) return ImportPlanBuilder.SupportedExtensions.Contains(Path.GetExtension(path)) ? Path.GetFullPath(path) : null;
        if (!Directory.Exists(path)) return null;
        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Where(file => ImportPlanBuilder.SupportedExtensions.Contains(Path.GetExtension(file))).Take(2).ToArray();
        return files.Length == 1 ? Path.GetFullPath(files[0]) : null;
    }

    private static AtomicUpgradeFileSnapshot Snapshot(string path) {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0) throw new IOException("The replacement files must be available and nonempty.");
        return new(info.Length, info.LastWriteTimeUtc);
    }

    private static bool Matches(string path, AtomicUpgradeFileSnapshot snapshot) {
        var info = new FileInfo(path);
        return info.Exists && info.Length == snapshot.Length && info.LastWriteTimeUtc == snapshot.LastWriteTimeUtc;
    }

    private static async Task<bool> SameBytesAsync(string first, string second, CancellationToken cancellationToken) {
        if (!File.Exists(first) || !File.Exists(second) || new FileInfo(first).Length != new FileInfo(second).Length) return false;
        await using var left = File.OpenRead(first);
        await using var right = File.OpenRead(second);
        var hash = await SHA256.HashDataAsync(left, cancellationToken);
        var otherHash = await SHA256.HashDataAsync(right, cancellationToken);
        return hash.AsSpan().SequenceEqual(otherHash);
    }
}
