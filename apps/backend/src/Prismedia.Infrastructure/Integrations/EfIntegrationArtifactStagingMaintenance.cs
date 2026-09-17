using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Deletes only allowlisted private staging files after revalidating terminal durable import evidence.</summary>
public sealed class EfIntegrationArtifactStagingMaintenance(
    PrismediaDbContext db,
    IntegrationArtifactStorageOptions options) : IIntegrationArtifactStagingMaintenance {
    /// <inheritdoc />
    public async Task<int> SweepAsync(DateTimeOffset completedBefore, CancellationToken cancellationToken) {
        var root = Path.GetFullPath(options.RootPath);
        if (!Directory.Exists(root)) return 0;
        RejectLink(root);
        var deleted = 0;
        foreach (var directory in Directory.EnumerateDirectories(root)) {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (!Guid.TryParseExact(name, "N", out var operationId)
                || !Path.GetFullPath(directory).Equals(Path.Combine(root, operationId.ToString("N")), FileSystemPathComparison.Comparison)) continue;
            try {
                if (await TryDeleteAsync(operationId, directory, completedBefore, cancellationToken)) deleted++;
            } catch (InvalidDataException) {
                // Untrusted state or filesystem links make the whole operation ineligible.
            } catch (IOException) {
                // A live transfer or scanner owns one of these files. A later sweep can retry.
            } catch (UnauthorizedAccessException) {
                // Filesystem ownership may be changing during startup; retain staging and retry later.
            }
        }
        return deleted;
    }

    private async Task<bool> TryDeleteAsync(Guid operationId, string directory, DateTimeOffset cutoff, CancellationToken token) {
        RejectLink(directory);
        var row = await ReadEligibleAsync(operationId, cutoff, token);
        if (row is null) return false;
        var state = Deserialize(row);
        if (!HasCompleteImportEvidence(row, state)) return false;
        var lockedRevision = row.Revision;
        var lockedArtifacts = state.Artifacts!.ToArray();
        var allowed = AllowedNames(state.Artifacts!);
        if (!HasOnlyAllowedFiles(directory, allowed)) return false;

        var locks = new List<FileStream>();
        try {
            foreach (var artifact in state.Artifacts!.OrderBy(item => item.Id, StringComparer.Ordinal)) {
                var lockPath = Path.Combine(directory, HttpIntegrationArtifactTransfer.ArtifactKey(artifact.Id) + ".lock");
                RejectLink(lockPath);
                locks.Add(new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            // The database and directory are checked again only after every byte-layer lock is owned.
            db.ChangeTracker.Clear();
            row = await ReadEligibleAsync(operationId, cutoff, token);
            if (row is null) return false;
            state = Deserialize(row);
            if (row.Revision != lockedRevision || !HasCompleteImportEvidence(row, state)
                || !state.Artifacts!.SequenceEqual(lockedArtifacts)
                || !HasOnlyAllowedFiles(directory, AllowedNames(state.Artifacts!))) return false;

            foreach (var entry in Directory.EnumerateFiles(directory)) {
                token.ThrowIfCancellationRequested();
                if (!entry.EndsWith(".lock", StringComparison.Ordinal)) File.Delete(entry);
            }
        } finally {
            foreach (var handle in locks) await handle.DisposeAsync();
        }

        if (!Directory.Exists(directory)) return true;
        foreach (var entry in Directory.EnumerateFiles(directory)) {
            RejectLink(entry);
            if (!entry.EndsWith(".lock", StringComparison.Ordinal)) return false;
            File.Delete(entry);
        }
        if (Directory.EnumerateFileSystemEntries(directory).Any()) return false;
        Directory.Delete(directory, recursive: false);
        return true;
    }

    private async Task<IntegrationTransferRow?> ReadEligibleAsync(Guid id, DateTimeOffset cutoff, CancellationToken token) {
        var row = await db.IntegrationTransfers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id
            && item.Phase == IntegrationTransferPhase.Completed && item.UpdatedAt <= cutoff, token);
        if (row is null) return null;
        var target = id.ToString();
        var active = await db.JobRuns.AsNoTracking().AnyAsync(job => job.Type == JobType.IntegrationTransfer
            && job.TargetEntityId == target && (job.Status == JobRunStatus.Queued || job.Status == JobRunStatus.Running), token);
        return active ? null : row;
    }

    private static IntegrationTransferState Deserialize(IntegrationTransferRow row) {
        try {
            return JsonSerializer.Deserialize<IntegrationTransferState>(row.StateJson, PluginProcessTransport.JsonOptions)
                ?? throw new InvalidDataException("Integration transfer state is empty.");
        } catch (JsonException exception) {
            throw new InvalidDataException("Integration transfer state is invalid.", exception);
        }
    }

    private static bool HasCompleteImportEvidence(IntegrationTransferRow row, IntegrationTransferState state) {
        if (row.Phase != IntegrationTransferPhase.Completed || state.OperationId != row.Id || state.ConnectionId != row.ConnectionId
            || state.Revision != row.Revision || state.Phase != IntegrationTransferPhase.Completed || state.ReceiptId is not { } receipt || receipt == Guid.Empty
            || state.Artifacts is not { Count: > 0 } artifacts || state.Imports is not { } imports || imports.Count != artifacts.Count
            || artifacts.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != artifacts.Count
            || imports.Select(item => item.ArtifactId).Distinct(StringComparer.Ordinal).Count() != imports.Count) return false;
        if (state.Mode == IntegrationTransferMode.RemoteExecutor && state.ReceiptId is null) return false;
        var imported = imports.ToDictionary(item => item.ArtifactId, StringComparer.Ordinal);
        return artifacts.All(artifact => imported.TryGetValue(artifact.Id, out var evidence)
            && evidence.Sha256.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase)
            && evidence.EntityIds is not null && evidence.EntityIds.All(id => id != Guid.Empty)
            && evidence.ContainerEntityId != Guid.Empty
            && (artifact.Role != IntegrationArtifactRole.Content || evidence.EntityIds.Count > 0));
    }

    private static HashSet<string> AllowedNames(IReadOnlyList<IntegrationArtifact> artifacts) {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in artifacts) {
            var key = HttpIntegrationArtifactTransfer.ArtifactKey(artifact.Id);
            allowed.Add(key + Path.GetExtension(artifact.RelativePath).ToLowerInvariant());
            allowed.Add(key + ".partial");
            allowed.Add(key + ".verified.json");
            allowed.Add(key + ".verified.json.tmp");
            allowed.Add(key + ".lock");
        }
        return allowed;
    }

    private static bool HasOnlyAllowedFiles(string directory, HashSet<string> allowed) {
        RejectLink(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory)) {
            RejectLink(entry);
            if (Directory.Exists(entry) || !allowed.Contains(Path.GetFileName(entry))) return false;
        }
        return true;
    }

    private static void RejectLink(string path) {
        FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (entry.LinkTarget is not null || entry.Exists && entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Artifact staging cleanup cannot traverse filesystem links.");
    }
}
