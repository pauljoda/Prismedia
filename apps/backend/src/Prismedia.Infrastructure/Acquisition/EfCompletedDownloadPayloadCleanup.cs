using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Validates retained import evidence before removing completed files a downloader's API leaves behind.</summary>
public sealed class EfCompletedDownloadPayloadCleanup(PrismediaDbContext db, RemotePathMapper paths) {
    private static readonly AcquisitionStatus[] Terminal = [AcquisitionStatus.Imported, AcquisitionStatus.Failed,
        AcquisitionStatus.Cancelled, AcquisitionStatus.Stopping];

    /// <summary>Runs inside the caller's ownership lock, before remote history deletion makes the path unavailable.</summary>
    public async Task DeleteAsync(IDownloadClient client, DownloadClientConnection connection, Guid? acquisitionId, string itemId,
        DownloadItemStatus? live, CancellationToken token) {
        if (live is { IsComplete: false }) return;
        var transfers = await db.DownloadTransfers.AsNoTracking().Where(row => row.AcquisitionId == acquisitionId
            && row.DownloadClientConfigId == connection.Id && row.ClientItemId == itemId).ToArrayAsync(token);
        var receipts = await db.DetachedDownloadCleanups.AsNoTracking().Where(row => row.SourceAcquisitionId == acquisitionId
            && row.DownloadClientConfigId == connection.Id && row.ClientItemId == itemId).ToArrayAsync(token);
        var recorded = transfers.Where(row => row.Progress >= 1).Select(row => row.ContentPath)
            .Concat(receipts.Select(row => row.ContentPath)).Where(path => !string.IsNullOrWhiteSpace(path)).Cast<string>().ToList();
        var reported = await paths.ToLocalAsync(connection.Id, live?.ContentPath, token);
        if (!string.IsNullOrWhiteSpace(reported)) recorded.Add(reported);
        if (recorded.Count == 0) {
            if (live is { IsComplete: true }) throw new IOException("The completed download has no usable local payload path.");
            return;
        }
        var canonical = recorded.Select(path => CompletedPayloadFileSystem.CanonicalPath(path, rejectLeafLink: true))
            .Distinct(FileSystemPathComparison.Comparer).ToArray();
        if (canonical.Length != 1) throw new IOException("The download's payload path changed before cleanup; its files were preserved.");
        var payload = canonical[0];
        var ownershipKey = $"{connection.Id:N}:{itemId}";
        if (!CompletedPayloadFileSystem.HasPayload(payload, ownershipKey)) return;
        var completedRoots = new List<string>();
        if (!string.IsNullOrWhiteSpace(connection.DownloadDirectory)) {
            completedRoots.Add(CompletedPayloadFileSystem.CanonicalPath(connection.DownloadDirectory));
        } else {
            foreach (var remoteRoot in await client.GetCompletedDirectoriesAsync(connection, token)) {
                var localRoot = await paths.ToLocalAsync(connection.Id, remoteRoot, token);
                if (!string.IsNullOrWhiteSpace(localRoot)) completedRoots.Add(CompletedPayloadFileSystem.CanonicalPath(localRoot));
            }
        }
        if (completedRoots.Any(root => FileSystemPathComparison.Equals(root, Path.GetPathRoot(root)!))
            || !completedRoots.Any(root => FileSystemPathComparison.IsSameOrDescendant(root, payload))
            || completedRoots.Any(root => FileSystemPathComparison.IsSameOrDescendant(payload, root))) {
            throw new IOException("The recorded payload is not beneath its downloader's completed directory; its files were preserved.");
        }
        var raw = recorded[0];
        var prefix = payload + Path.DirectorySeparatorChar;
        var rawPrefix = raw.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var related = await (from transfer in db.DownloadTransfers.AsNoTracking()
            join owner in db.Acquisitions.AsNoTracking() on transfer.AcquisitionId equals owner.Id
            where transfer.AcquisitionId != acquisitionId && transfer.ContentPath != null
                && (!Terminal.Contains(owner.Status) || transfer.ContentPath == payload || transfer.ContentPath == raw
                    || transfer.ContentPath.StartsWith(prefix) || transfer.ContentPath.StartsWith(rawPrefix)
                    || payload.StartsWith(transfer.ContentPath + Path.DirectorySeparatorChar)
                    || raw.StartsWith(transfer.ContentPath + Path.DirectorySeparatorChar))
            select new { transfer.AcquisitionId, transfer.DownloadClientConfigId, transfer.ClientItemId, transfer.ContentPath, owner.Status, transfer.SeedingSince,
                transfer.SeedGoalRatio, transfer.SeedGoalTimeMinutes }).ToArrayAsync(token);
        var shared = related.Where(row => CompletedPayloadFileSystem.Overlaps(payload,
            CompletedPayloadFileSystem.CanonicalPath(row.ContentPath!))).ToArray();
        if (shared.Any(row => !Terminal.Contains(row.Status)
            || (row.DownloadClientConfigId != connection.Id || !string.Equals(row.ClientItemId, itemId, StringComparison.OrdinalIgnoreCase))
                && row.SeedingSince is not null && (row.SeedGoalRatio is not null || row.SeedGoalTimeMinutes is not null))) {
            throw new IOException("Another acquisition still needs files in this payload; they were preserved.");
        }
        var roots = await db.LibraryRoots.AsNoTracking().ToArrayAsync(token);
        var parent = Path.GetDirectoryName(payload)! + Path.DirectorySeparatorChar;
        var rawParent = Path.GetDirectoryName(raw)! + Path.DirectorySeparatorChar;
        var protectedFiles = await db.EntityFiles.AsNoTracking()
            .Where(row => row.Path.StartsWith(parent) || row.Path.StartsWith(rawParent))
            .Select(row => row.Path).ToArrayAsync(token);
        var ownerIds = shared.Where(row => row.Status == AcquisitionStatus.Imported).Select(row => row.AcquisitionId)
            .Concat(acquisitionId is { } id ? [id] : []).Distinct().ToArray();
        var owners = await db.Acquisitions.AsNoTracking().Where(row => ownerIds.Contains(row.Id)).ToArrayAsync(token);
        var required = new List<string>();
        foreach (var owner in owners) {
            if (owner.UpgradeOfAcquisitionId is { } parentId && string.IsNullOrWhiteSpace(owner.FinalSourcePath)) {
                var upgradedSource = await db.Acquisitions.AsNoTracking().Where(row => row.Id == parentId)
                    .Select(row => row.FinalSourcePath).FirstOrDefaultAsync(token);
                await AddRequiredSourcesAsync(upgradedSource, null, null, true, roots, required, token);
                continue;
            }
            await AddRequiredSourcesAsync(owner.FinalSourcePath, owner.ImportResultJson, owner.TargetLibraryRootId,
                owner.Status == AcquisitionStatus.Imported, roots, required, token);
        }
        foreach (var receipt in receipts.Where(row => !string.IsNullOrWhiteSpace(row.ImportedSourcePath))) {
            await AddRequiredSourcesAsync(receipt.ImportedSourcePath, null, null, true, roots, required, token);
        }
        CompletedPayloadFileSystem.Delete(payload, ownershipKey, roots.Select(root => root.Path)
            .Concat(protectedFiles).ToArray(), required.Distinct(FileSystemPathComparison.Comparer).ToArray(), token);
    }

    private async Task AddRequiredSourcesAsync(string? finalSource, string? ledgerJson, Guid? rootId, bool imported,
        LibraryRootRow[] roots, List<string> required, CancellationToken token) {
        if (AcquisitionImportFileLedgerJson.TryDeserialize(ledgerJson, out var ledger) && ledger is not null) {
            var placed = ledger.Files.Where(file => file.Role == AcquisitionImportFileRole.Media
                && file.Status == AcquisitionImportFileStatus.Imported).ToArray();
            if (placed.Length > 0) {
                var root = roots.FirstOrDefault(candidate => candidate.Id == rootId)
                    ?? roots.Where(candidate => finalSource is not null && FileSystemPathComparison.IsSameOrDescendant(candidate.Path, finalSource))
                        .OrderByDescending(candidate => candidate.Path.Length).FirstOrDefault();
                if (root is null) throw new IOException("The imported file ledger's library root is unavailable; the payload was preserved.");
                foreach (var file in placed) {
                    if (string.IsNullOrWhiteSpace(file.DestinationRelativePath)) throw new IOException("The imported file ledger has no destination.");
                    var target = Path.GetFullPath(Path.Combine(root.Path, file.DestinationRelativePath));
                    if (!FileSystemPathComparison.IsSameOrDescendant(root.Path, target)) throw new IOException("An import destination escapes its library root.");
                    required.Add(target);
                }
                return;
            }
        }
        if (!string.IsNullOrWhiteSpace(finalSource)) {
            if (Directory.Exists(finalSource)) {
                var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(finalSource)) + Path.DirectorySeparatorChar;
                var files = await db.EntityFiles.AsNoTracking().Where(row => row.Role == EntityFileRole.Source && row.Path.StartsWith(prefix))
                    .Select(row => row.Path).ToArrayAsync(token);
                if (files.Length == 0) throw new IOException("The imported folder has no recorded source files; its payload was preserved.");
                required.AddRange(files);
            } else required.Add(finalSource);
        } else if (imported) {
            throw new IOException("The imported acquisition has no verifiable library copy; its payload was preserved.");
        }
    }
}
