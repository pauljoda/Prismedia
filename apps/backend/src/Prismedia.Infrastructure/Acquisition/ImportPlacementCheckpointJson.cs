using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Stable codec-enum JSON persistence for resumable book, movie, and album placements.</summary>
internal static class ImportPlacementCheckpointJson {
    private static readonly JsonSerializerOptions Options = new() {
        Converters = { new CodecJsonConverterFactory() }
    };

    public static string Serialize(ImportPlacementCheckpoint checkpoint) {
        Validate(checkpoint);
        return JsonSerializer.Serialize(checkpoint, Options);
    }

    public static ImportPlacementCheckpoint? Deserialize(string? json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return null;
        }

        try {
            var checkpoint = JsonSerializer.Deserialize<ImportPlacementCheckpoint>(json, Options)
                ?? throw new InvalidDataException("The import placement checkpoint decoded to an empty value.");
            Validate(checkpoint);
            return checkpoint;
        } catch (InvalidDataException) {
            throw;
        } catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException) {
            throw new InvalidDataException(
                "The durable import placement checkpoint is malformed or incompatible and cannot be resumed safely.",
                ex);
        }
    }

    /// <summary>
    /// Ensures a compare-and-swap can only mark pending units complete. Immutable plan facts and already
    /// completed units may never change between writes.
    /// </summary>
    public static bool IsValidAdvance(
        ImportPlacementCheckpoint expected,
        ImportPlacementCheckpoint advanced) {
        if (expected.Kind != advanced.Kind
            || expected.LibraryRootId != advanced.LibraryRootId
            || !PathEquals(expected.LibraryRootPath, advanced.LibraryRootPath)
            || !PathEquals(expected.PayloadRootPath, advanced.PayloadRootPath)
            || expected.ImportMode != advanced.ImportMode
            || !PathEquals(expected.HintPath, advanced.HintPath)
            || !PathEquals(expected.FinalSourcePath, advanced.FinalSourcePath)
            || !string.Equals(expected.SuccessMessage, advanced.SuccessMessage, StringComparison.Ordinal)
            || !string.Equals(expected.TransferClientItemId, advanced.TransferClientItemId, StringComparison.Ordinal)
            || expected.AttemptId != advanced.AttemptId
            || expected.ClaimJobId != advanced.ClaimJobId
            || expected.Units.Count != advanced.Units.Count) {
            return false;
        }

        var newlyCompleted = 0;
        for (var index = 0; index < expected.Units.Count; index++) {
            var before = expected.Units[index];
            var after = advanced.Units[index];
            if (!string.Equals(before.SourceRelativePath, after.SourceRelativePath, StringComparison.Ordinal)
                || !PathEquals(before.SourceAbsolutePath, after.SourceAbsolutePath)
                || !PathEquals(before.TargetAbsolutePath, after.TargetAbsolutePath)
                || before.IsMedia != after.IsMedia) {
                return false;
            }

            if (before.FinalPath is not null) {
                if (after.FinalPath is null || !PathEquals(before.FinalPath, after.FinalPath)) {
                    return false;
                }
                continue;
            }

            if (after.FinalPath is not null) {
                newlyCompleted++;
            }
        }

        return newlyCompleted == 1;
    }

    private static void Validate(ImportPlacementCheckpoint checkpoint) {
        if (checkpoint.Kind is not (EntityKind.Book or EntityKind.Movie or EntityKind.AudioLibrary)) {
            Invalid("Kind must be book, movie, or audio library.");
        }
        if (checkpoint.LibraryRootId == Guid.Empty) {
            Invalid("LibraryRootId must be non-empty.");
        }
        if (checkpoint.AttemptId == Guid.Empty) {
            Invalid("AttemptId must be non-empty.");
        }
        if (checkpoint.ClaimJobId == Guid.Empty) {
            Invalid("ClaimJobId must be non-empty.");
        }
        if (string.IsNullOrWhiteSpace(checkpoint.SuccessMessage)) {
            Invalid("SuccessMessage must be non-empty.");
        }
        if (checkpoint.TransferClientItemId is not null
            && string.IsNullOrWhiteSpace(checkpoint.TransferClientItemId)) {
            Invalid("TransferClientItemId cannot be blank when present.");
        }

        var root = RequireAbsolutePath(checkpoint.LibraryRootPath, nameof(checkpoint.LibraryRootPath));
        var payloadRoot = RequireAbsolutePath(checkpoint.PayloadRootPath, nameof(checkpoint.PayloadRootPath));
        var hint = RequireAbsolutePath(checkpoint.HintPath, nameof(checkpoint.HintPath));
        var finalSource = RequireAbsolutePath(checkpoint.FinalSourcePath, nameof(checkpoint.FinalSourcePath));
        if (!IsAtOrUnder(hint, root)) {
            Invalid("HintPath must be inside LibraryRootPath.");
        }
        if (!IsAtOrUnder(finalSource, root)) {
            Invalid("FinalSourcePath must be inside LibraryRootPath.");
        }
        if (checkpoint.Units is not { Count: > 0 }) {
            Invalid("Units must contain at least one recovery unit.");
        }
        if (!checkpoint.Units.Any(unit => unit?.IsMedia == true)) {
            Invalid("Units must contain at least one media file.");
        }

        var sourcePaths = new HashSet<string>(PathComparer);
        var targetPaths = new HashSet<string>(PathComparer);
        for (var index = 0; index < checkpoint.Units.Count; index++) {
            var unit = checkpoint.Units[index];
            if (unit is null) {
                Invalid($"Units[{index}] cannot be null.");
                continue;
            }

            RequireRelativePath(unit.SourceRelativePath, $"Units[{index}].SourceRelativePath");
            var source = RequireAbsolutePath(unit.SourceAbsolutePath, $"Units[{index}].SourceAbsolutePath");
            var target = RequireAbsolutePath(unit.TargetAbsolutePath, $"Units[{index}].TargetAbsolutePath");
            var expectedSource = Path.GetFullPath(Path.Combine(payloadRoot, unit.SourceRelativePath));
            if (!IsAtOrUnder(source, payloadRoot) || !PathEquals(source, expectedSource)) {
                Invalid($"Units[{index}].SourceAbsolutePath must exactly match PayloadRootPath plus SourceRelativePath.");
            }
            if (!IsAtOrUnder(target, root) || PathEquals(target, root)) {
                Invalid($"Units[{index}].TargetAbsolutePath must be a file inside LibraryRootPath.");
            }
            if (!sourcePaths.Add(source)) {
                Invalid($"Units[{index}].SourceAbsolutePath duplicates another unit.");
            }
            if (!targetPaths.Add(target)) {
                Invalid($"Units[{index}].TargetAbsolutePath duplicates another unit.");
            }

            if (unit.FinalPath is not null) {
                var finalPath = RequireAbsolutePath(unit.FinalPath, $"Units[{index}].FinalPath");
                if (!PathEquals(finalPath, target)) {
                    Invalid($"Units[{index}].FinalPath must exactly match TargetAbsolutePath.");
                }
            }
        }
    }

    private static void RequireRelativePath(string? path, string field) {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathFullyQualified(path)) {
            Invalid($"{field} must be a non-empty relative path.");
        }

        var segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
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
                $"The durable import placement checkpoint cannot be resumed safely because {field} is invalid.",
                ex);
        }
    }

    private static bool IsAtOrUnder(string candidate, string folder) =>
        PathEquals(candidate, folder) || candidate.StartsWith(
            folder.EndsWith(Path.DirectorySeparatorChar)
                ? folder
                : folder + Path.DirectorySeparatorChar,
            FileSystemPathComparison.Comparison);

    private static bool PathEquals(string first, string second) =>
        FileSystemPathComparison.Equals(first, second);

    private static StringComparer PathComparer => FileSystemPathComparison.Comparer;

    [DoesNotReturn]
    private static void Invalid(string reason) =>
        throw new InvalidDataException(
            $"The durable import placement checkpoint is malformed or incompatible and cannot be resumed safely: {reason}");
}
