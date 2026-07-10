using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Stable codec-enum JSON persistence for resumable TV import checkpoints.</summary>
internal static class TvImportCheckpointJson {
    private static readonly JsonSerializerOptions Options = new() {
        Converters = { new CodecJsonConverterFactory() }
    };

    public static string Serialize(TvImportCheckpoint checkpoint) =>
        JsonSerializer.Serialize(checkpoint, Options);

    public static TvImportCheckpoint? Deserialize(string? json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return null;
        }

        try {
            var checkpoint = JsonSerializer.Deserialize<TvImportCheckpoint>(json, Options)
                ?? throw new InvalidDataException("The TV import checkpoint decoded to an empty value.");
            Validate(checkpoint);
            return checkpoint;
        } catch (InvalidDataException) {
            throw;
        } catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException) {
            throw new InvalidDataException(
                "The durable TV import checkpoint is malformed or incompatible and cannot be resumed safely.",
                ex);
        }
    }

    private static void Validate(TvImportCheckpoint checkpoint) {
        if (checkpoint.LibraryRootId == Guid.Empty) {
            Invalid("LibraryRootId must be non-empty.");
        }
        if (checkpoint.AttemptId == Guid.Empty) {
            Invalid("AttemptId must be non-empty.");
        }
        if (checkpoint.ClaimJobId == Guid.Empty) {
            Invalid("ClaimJobId must be non-empty.");
        }

        var seriesFolder = RequireAbsolutePath(checkpoint.SeriesFolderPath, nameof(checkpoint.SeriesFolderPath));
        if (checkpoint.TransferClientItemId is not null && string.IsNullOrWhiteSpace(checkpoint.TransferClientItemId)) {
            Invalid("TransferClientItemId cannot be blank when present.");
        }
        if (checkpoint.Units is not { Count: > 0 }) {
            Invalid("Units must contain at least one recovery unit.");
        }

        for (var index = 0; index < checkpoint.Units.Count; index++) {
            ValidateUnit(checkpoint, checkpoint.Units[index], index, seriesFolder);
        }
    }

    private static void ValidateUnit(
        TvImportCheckpoint checkpoint,
        TvImportCheckpointUnit? unit,
        int index,
        string seriesFolder) {
        var prefix = $"Units[{index}]";
        if (unit is null) {
            Invalid($"{prefix} cannot be null.");
            return;
        }

        RequireRelativePath(unit.SourceRelativePath, $"{prefix}.SourceRelativePath");
        var sourceAbsolute = RequireAbsolutePath(unit.SourceAbsolutePath, $"{prefix}.SourceAbsolutePath");
        var target = RequireAbsolutePath(unit.TargetAbsolutePath, $"{prefix}.TargetAbsolutePath");
        if (!IsUnderFolder(target, seriesFolder)) {
            Invalid($"{prefix}.TargetAbsolutePath must be inside SeriesFolderPath.");
        }
        if (unit.SeasonNumber <= 0) {
            Invalid($"{prefix}.SeasonNumber must be positive.");
        }
        if (unit.EpisodeNumber <= 0) {
            Invalid($"{prefix}.EpisodeNumber must be positive.");
        }
        if (unit.CoveredEpisodeNumbers is null
            || unit.CoveredEpisodeNumbers.Any(episode => episode <= 0 || episode == unit.EpisodeNumber)
            || unit.CoveredEpisodeNumbers.Distinct().Count() != unit.CoveredEpisodeNumbers.Count) {
            Invalid($"{prefix}.CoveredEpisodeNumbers must contain distinct positive additional episodes.");
        }

        if (unit.FinalPath is not null) {
            var finalPath = RequireAbsolutePath(unit.FinalPath, $"{prefix}.FinalPath");
            if (!PathEquals(finalPath, target)) {
                Invalid($"{prefix}.FinalPath must exactly match TargetAbsolutePath.");
            }
        }

        if (unit.PreviousFilePath is null) {
            if (unit.ReplacementBackupPath is not null || unit.ReplacementEvidencePath is not null) {
                Invalid($"{prefix} cannot carry replacement artifacts without PreviousFilePath.");
            }
            return;
        }

        var previous = RequireAbsolutePath(unit.PreviousFilePath, $"{prefix}.PreviousFilePath");
        if (!IsUnderFolder(previous, seriesFolder)) {
            Invalid($"{prefix}.PreviousFilePath must be inside SeriesFolderPath.");
        }

        var sourceExtension = Path.GetExtension(unit.SourceRelativePath);
        if (string.IsNullOrWhiteSpace(sourceExtension)) {
            Invalid($"{prefix}.SourceRelativePath must include a file extension.");
        }
        if (!string.Equals(Path.GetExtension(sourceAbsolute), sourceExtension, StringComparison.OrdinalIgnoreCase)) {
            Invalid($"{prefix}.SourceAbsolutePath must use the SourceRelativePath extension.");
        }

        var expectedTarget = Path.ChangeExtension(previous, sourceExtension);
        if (!PathEquals(target, expectedTarget)) {
            Invalid($"{prefix}.TargetAbsolutePath is not the exact replacement target.");
        }
        if (!checkpoint.AllowFormatChange
            && !string.Equals(Path.GetExtension(previous), sourceExtension, StringComparison.OrdinalIgnoreCase)) {
            Invalid($"{prefix} changes format without checkpoint consent.");
        }

        var backup = RequireAbsolutePath(unit.ReplacementBackupPath, $"{prefix}.ReplacementBackupPath");
        var expectedBackup = OwnedFileReplacementArtifacts.CheckpointBackupPath(previous, checkpoint.AttemptId);
        if (!PathEquals(backup, expectedBackup)) {
            Invalid($"{prefix}.ReplacementBackupPath is not canonical for AttemptId.");
        }

        var evidence = RequireAbsolutePath(unit.ReplacementEvidencePath, $"{prefix}.ReplacementEvidencePath");
        var expectedEvidence = OwnedFileReplacementArtifacts.CheckpointEvidencePath(previous, checkpoint.AttemptId);
        if (!PathEquals(evidence, expectedEvidence)) {
            Invalid($"{prefix}.ReplacementEvidencePath is not canonical for AttemptId.");
        }
    }

    private static void RequireRelativePath(string? path, string field) {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathFullyQualified(path)) {
            Invalid($"{field} must be a non-empty relative path.");
        }

        var segments = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or "..")) {
            Invalid($"{field} cannot traverse outside the payload root.");
        }
    }

    private static string RequireAbsolutePath(string? path, string field) {
        if (string.IsNullOrWhiteSpace(path)) {
            Invalid($"{field} must be non-empty.");
        }

        try {
            if (!Path.IsPathFullyQualified(path)) {
                Invalid($"{field} must be an absolute path.");
            }

            var fullPath = Path.GetFullPath(path);
            if (!PathEquals(path, fullPath)) {
                Invalid($"{field} must be an exact normalized path.");
            }
            return fullPath;
        } catch (InvalidDataException) {
            throw;
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) {
            throw new InvalidDataException(
                $"The durable TV import checkpoint cannot be resumed safely because {field} is invalid.",
                ex);
        }
    }

    private static bool IsUnderFolder(string candidate, string folder) {
        var prefix = folder.EndsWith(Path.DirectorySeparatorChar)
            ? folder
            : folder + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, FileSystemPathComparison.Comparison);
    }

    private static bool PathEquals(string first, string second) =>
        FileSystemPathComparison.Equals(first, second);

    [DoesNotReturn]
    private static void Invalid(string reason) =>
        throw new InvalidDataException(
            $"The durable TV import checkpoint is malformed or incompatible and cannot be resumed safely: {reason}");
}
