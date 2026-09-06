using System.Text.Json;
using System.Text.Json.Serialization;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Strict persistence boundary for prepared atomic replacements; damaged evidence is never guessed.</summary>
internal static class AtomicUpgradeCheckpointJson {
    private static readonly JsonSerializerOptions Options = new() {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new CodecJsonConverterFactory() }
    };

    public static bool IsAtomic(string? json) {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(nameof(AtomicUpgradeCheckpoint.Protocol), out var protocol)
                && protocol.ValueKind == JsonValueKind.String
                && protocol.GetString() == AcquisitionCheckpointProtocol.AtomicUpgrade.ToCode();
        } catch (JsonException exception) {
            throw new InvalidDataException("The saved import checkpoint is malformed and cannot be resumed safely.", exception);
        }
    }

    public static string Serialize(AtomicUpgradeCheckpoint checkpoint) {
        Validate(checkpoint);
        return JsonSerializer.Serialize(checkpoint, Options);
    }

    public static AtomicUpgradeCheckpoint Deserialize(string json) {
        try {
            if (!IsAtomic(json)) throw new InvalidDataException("The saved replacement checkpoint has no valid protocol discriminator.");
            var checkpoint = JsonSerializer.Deserialize<AtomicUpgradeCheckpoint>(json, Options)
                ?? throw new InvalidDataException("The saved replacement checkpoint is empty.");
            Validate(checkpoint);
            return checkpoint;
        } catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException) {
            throw new InvalidDataException("The saved replacement checkpoint is malformed or incompatible.", exception);
        }
    }

    private static void Validate(AtomicUpgradeCheckpoint checkpoint) {
        if (checkpoint.Protocol != AcquisitionCheckpointProtocol.AtomicUpgrade
            || checkpoint.AttemptId == Guid.Empty || checkpoint.ClaimJobId == Guid.Empty
            || checkpoint.ParentAcquisitionId == Guid.Empty || checkpoint.ParentEntityId == Guid.Empty
            || checkpoint.SourceFileId == Guid.Empty
            || EntityKindRegistry.Describe(checkpoint.Kind).UpgradeMode is not (EntityUpgradeMode.AtomicBookFile or EntityUpgradeMode.AtomicMediaFile)
            || checkpoint.Files is not { Owned.Length: > 0, Incoming.Length: > 0 } files
            || !CanonicalPath(files.OwnedPath) || !CanonicalPath(files.IncomingPath)
            || FileSystemPathComparison.Equals(files.OwnedPath, files.IncomingPath)
            || !CanonicalPath(checkpoint.TransferContentPath)
            || !FileSystemPathComparison.IsSameOrDescendant(checkpoint.TransferContentPath, files.IncomingPath)
            || files.Owned.LastWriteTimeUtc.Kind != DateTimeKind.Utc || files.Incoming.LastWriteTimeUtc.Kind != DateTimeKind.Utc
            || checkpoint.SelectedRelease is null || string.IsNullOrWhiteSpace(checkpoint.SelectedRelease.Title)
            || checkpoint.TransferClientItemId is not null && string.IsNullOrWhiteSpace(checkpoint.TransferClientItemId)) {
            throw new InvalidDataException("The saved replacement checkpoint contains invalid identity, file, or transfer evidence.");
        }
    }

    private static bool CanonicalPath(string path) => !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path) && FileSystemPathComparison.Equals(path, Path.GetFullPath(path));
}
