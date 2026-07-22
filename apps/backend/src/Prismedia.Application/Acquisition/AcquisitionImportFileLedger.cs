using System.Security.Cryptography;
using System.Text;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

public sealed record AcquisitionImportFileLedger(
    AcquisitionImportPhase Phase,
    IReadOnlyList<AcquisitionImportFileLedgerEntry> Files) {
    public static AcquisitionImportFileLedger Create(ImportPlacementCheckpoint checkpoint) => new(
        AcquisitionImportPhase.Importing,
        checkpoint.Units.Select(unit => CreateEntry(
            unit.SourceRelativePath,
            unit.TargetAbsolutePath,
            checkpoint.LibraryRootPath,
            checkpoint.PayloadRootPath,
            unit.IsMedia,
            unit.FinalPath is null ? AcquisitionImportFileStatus.PendingImport : AcquisitionImportFileStatus.Imported,
            AcquisitionImportDecision.PlaceNew)).ToArray());

    public static AcquisitionImportFileLedger Create(TvImportCheckpoint checkpoint, string libraryRootPath) => new(
        AcquisitionImportPhase.Importing,
        checkpoint.Units.Select(unit => {
            var source = Normalize(unit.SourceRelativePath);
            var destination = Normalize(Path.GetRelativePath(libraryRootPath, unit.TargetAbsolutePath));
            var decision = unit.AdoptedExistingTarget
                ? AcquisitionImportDecision.AdoptExisting
                : unit.PreviousFilePath is null
                    ? AcquisitionImportDecision.PlaceNew
                    : AcquisitionImportDecision.ReplaceUpgrade;
            return new AcquisitionImportFileLedgerEntry(
                StableId(source),
                Path.GetFileName(source),
                FileSizeAtAbsolute(unit.SourceAbsolutePath),
                source,
                destination,
                AcquisitionImportFileRole.Media,
                ContentKind(source),
                unit.FinalPath is null ? AcquisitionImportFileStatus.PendingImport : AcquisitionImportFileStatus.Imported,
                decision,
                null);
        }).ToArray());

    public static AcquisitionImportFileLedger Create(
        TvImportCheckpoint checkpoint,
        string libraryRootPath,
        IReadOnlyList<MergedImportItem> merged,
        bool reconciledExisting) => new(
        AcquisitionImportPhase.Importing,
        merged.Select(item => {
            var entry = CreateMergedEntry(item, libraryRootPath, PayloadRootFor(checkpoint, item.SourceRelativePath), true);
            return reconciledExisting
                ? entry with { Status = AcquisitionImportFileStatus.Imported, Decision = AcquisitionImportDecision.AdoptExisting }
                : entry;
        }).ToArray());

    public static AcquisitionImportFileLedger Create(
        ImportPlacementCheckpoint checkpoint,
        IReadOnlyList<MergedImportItem> merged) => new(
        AcquisitionImportPhase.Importing,
        merged.Select(item => CreateMergedEntry(
            item,
            checkpoint.LibraryRootPath,
            checkpoint.PayloadRootPath,
            checkpoint.Units.FirstOrDefault(unit => FileSystemPathComparison.Comparer.Equals(
                unit.SourceRelativePath, item.SourceRelativePath))?.IsMedia ?? true)).ToArray());

    public static AcquisitionImportFileLedger Synchronize(
        AcquisitionImportFileLedger ledger,
        ImportPlacementCheckpoint checkpoint) => ledger with {
        Files = ledger.Files.Select(file => {
            var unit = checkpoint.Units.FirstOrDefault(candidate => FileSystemPathComparison.Comparer.Equals(
                candidate.SourceRelativePath, file.SourceRelativePath));
            if (unit is null) { return file; }
            return file with {
                DestinationRelativePath = Normalize(Path.GetRelativePath(checkpoint.LibraryRootPath, unit.TargetAbsolutePath)),
                Status = unit.FinalPath is null ? AcquisitionImportFileStatus.PendingImport : AcquisitionImportFileStatus.Imported,
            };
        }).ToArray()
    };

    public static AcquisitionImportFileLedger Synchronize(
        AcquisitionImportFileLedger ledger,
        TvImportCheckpoint checkpoint,
        string libraryRootPath) => ledger with {
        Files = ledger.Files.Select(file => {
            var unit = checkpoint.Units.FirstOrDefault(candidate => FileSystemPathComparison.Comparer.Equals(
                candidate.SourceRelativePath, file.SourceRelativePath));
            if (unit is null) { return file; }
            return file with {
                SizeBytes = file.SizeBytes > 0 ? file.SizeBytes : FileSizeAtAbsolute(unit.SourceAbsolutePath),
                DestinationRelativePath = Normalize(Path.GetRelativePath(libraryRootPath, unit.TargetAbsolutePath)),
                Status = unit.FinalPath is null ? AcquisitionImportFileStatus.PendingImport : AcquisitionImportFileStatus.Imported,
                Decision = unit.AdoptedExistingTarget
                    ? AcquisitionImportDecision.AdoptExisting
                    : unit.PreviousFilePath is null ? file.Decision : AcquisitionImportDecision.ReplaceUpgrade,
            };
        }).ToArray()
    };

    public AcquisitionImportFileLedger Advance(string sourceRelativePath, string destinationRelativePath) =>
        this with {
            Files = Files.Select(file => string.Equals(file.SourceRelativePath, sourceRelativePath, StringComparison.Ordinal)
                ? file with {
                    DestinationRelativePath = Normalize(destinationRelativePath),
                    Status = AcquisitionImportFileStatus.Imported,
                    TechnicalError = null,
                }
                : file).ToArray()
        };

    public AcquisitionImportFileLedger Complete() => this with { Phase = AcquisitionImportPhase.Imported };

    public AcquisitionImportFileLedger WithDecision(AcquisitionImportDecision decision) => this with {
        Files = Files.Select(file => file with { Decision = decision }).ToArray()
    };

    public static AcquisitionImportContentKind ClassifyContentKind(string path) => ContentKind(path);

    public AcquisitionImportFileLedger Fail(string technicalError) {
        var failed = false;
        return this with {
            Files = Files.Select(file => {
                if (failed || file.Status is not (AcquisitionImportFileStatus.PendingImport or AcquisitionImportFileStatus.Importing)) {
                    return file;
                }
                failed = true;
                return file with { Status = AcquisitionImportFileStatus.Failed, TechnicalError = technicalError };
            }).ToArray()
        };
    }

    private static AcquisitionImportFileLedgerEntry CreateEntry(
        string sourceRelativePath,
        string destinationAbsolutePath,
        string libraryRootPath,
        string payloadRootPath,
        bool isMedia,
        AcquisitionImportFileStatus status,
        AcquisitionImportDecision decision) {
        var source = Normalize(sourceRelativePath);
        var destination = Normalize(Path.GetRelativePath(libraryRootPath, destinationAbsolutePath));
        return new AcquisitionImportFileLedgerEntry(
            StableId(source),
            Path.GetFileName(source),
            FileSizeAt(source, payloadRootPath),
            source,
            destination,
            isMedia ? AcquisitionImportFileRole.Media : AcquisitionImportFileRole.Companion,
            ContentKind(source),
            status,
            decision,
            null);
    }

    private static AcquisitionImportFileLedgerEntry CreateMergedEntry(
        MergedImportItem item,
        string libraryRootPath,
        string? payloadRootPath,
        bool isMedia) {
        var source = Normalize(item.SourceRelativePath);
        var decision = item.Action switch {
            MergeFileAction.PlaceNew => AcquisitionImportDecision.PlaceNew,
            MergeFileAction.ReplaceUpgrade => AcquisitionImportDecision.ReplaceUpgrade,
            MergeFileAction.DropNotUpgrade when item.OwnedFilePath is null => AcquisitionImportDecision.SkipExisting,
            MergeFileAction.DropNotUpgrade => AcquisitionImportDecision.SkipNotUpgrade,
            MergeFileAction.DropFormatChange => AcquisitionImportDecision.HoldFormatChange,
            MergeFileAction.HoldStructuralConflict => AcquisitionImportDecision.HoldStructuralConflict,
            _ => AcquisitionImportDecision.Ambiguous,
        };
        var status = item.Action is MergeFileAction.PlaceNew or MergeFileAction.ReplaceUpgrade
            ? AcquisitionImportFileStatus.PendingImport
            : item.Action == MergeFileAction.HoldStructuralConflict
                ? AcquisitionImportFileStatus.Failed
                : AcquisitionImportFileStatus.Skipped;
        return new AcquisitionImportFileLedgerEntry(
            StableId(source), Path.GetFileName(source), FileSizeAt(source, payloadRootPath), source,
            Normalize(Path.GetRelativePath(libraryRootPath, item.TargetAbsolutePath)),
            isMedia ? AcquisitionImportFileRole.Media : AcquisitionImportFileRole.Companion,
            ContentKind(source), status, decision, null);
    }

    private static string StableId(string sourceRelativePath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceRelativePath))).ToLowerInvariant()[..16];

    private static long FileSizeAt(string sourceRelativePath, string? payloadRootPath) {
        if (payloadRootPath is null) { return 0; }
        try { return new FileInfo(Path.Combine(payloadRootPath, sourceRelativePath)).Length; }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static long FileSizeAtAbsolute(string? path) {
        if (string.IsNullOrWhiteSpace(path)) { return 0; }
        try { return new FileInfo(path).Length; }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static string? PayloadRootFor(TvImportCheckpoint checkpoint, string sourceRelativePath) {
        var unit = checkpoint.Units.FirstOrDefault(candidate => FileSystemPathComparison.Comparer.Equals(
            candidate.SourceRelativePath, sourceRelativePath));
        if (unit?.SourceAbsolutePath is not { } sourceAbsolutePath) { return null; }
        var root = sourceAbsolutePath;
        foreach (var _ in sourceRelativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)) {
            root = Path.GetDirectoryName(root) ?? root;
        }
        return root;
    }

    private static AcquisitionImportContentKind ContentKind(string path) => Path.GetExtension(path).ToLowerInvariant() switch {
        ".epub" or ".pdf" or ".cbz" or ".cbr" or ".mobi" or ".azw3" => AcquisitionImportContentKind.Book,
        ".mp3" or ".m4a" or ".m4b" or ".flac" or ".aac" or ".ogg" or ".wav" => AcquisitionImportContentKind.Audio,
        ".mkv" or ".mp4" or ".m4v" or ".avi" or ".mov" or ".webm" => AcquisitionImportContentKind.Video,
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".avif" => AcquisitionImportContentKind.Image,
        ".srt" or ".ass" or ".ssa" or ".vtt" or ".sub" => AcquisitionImportContentKind.Subtitle,
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => AcquisitionImportContentKind.Archive,
        _ => AcquisitionImportContentKind.Other,
    };

    private static string Normalize(string path) {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment is "." or "..")
            ? segments.LastOrDefault() ?? string.Empty
            : string.Join('/', segments);
    }
}
