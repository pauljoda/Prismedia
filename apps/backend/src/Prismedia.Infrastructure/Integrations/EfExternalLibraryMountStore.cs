using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Files;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;
using Prismedia.Infrastructure.Settings;
using Prismedia.Infrastructure.Plugins;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Private application work areas that must never become an externally managed library.</summary>
public sealed record ExternalLibraryStorageOptions(string DataPath, string CachePath);

/// <summary>Creates immutable external boundaries with their paused watched roots, and observes local bytes without writing them.</summary>
public sealed class EfExternalLibraryMountStore(PrismediaDbContext db, ExternalLibraryStorageOptions storage, SettingsSnapshotCache cache) : IExternalLibraryMountStore {
    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalLibraryMount>> ListAsync(Guid connectionId, CancellationToken token) =>
        await (from mount in db.ExternalLibraryMounts.AsNoTracking()
               join root in db.LibraryRoots.AsNoTracking() on mount.LibraryRootId equals root.Id
               where mount.ConnectionId == connectionId
               orderby root.Label
               select new ExternalLibraryMount(mount.Id, mount.ConnectionId, root.Id, mount.RemoteRootId, mount.RemotePath, mount.LocalPath, root.Label)).ToArrayAsync(token);

    /// <inheritdoc />
    public async Task<ExternalLibraryMount> CreateAsync(Guid connectionId, long expectedRevision, CreateExternalLibraryMountRequest request, CancellationToken token) {
        if (string.IsNullOrWhiteSpace(request.LocalPath) || request.LocalPath.Length > 8192 || request.LocalPath.Any(char.IsControl)
            || !Path.IsPathFullyQualified(request.LocalPath) || !Directory.Exists(request.LocalPath))
            throw new ArgumentException("Choose an existing absolute local folder that Prismedia can read.");
        _ = ExternalLibraryPaths.NormalizeRemote(request.ExpectedRemotePath);
        var local = CompletedPayloadFileSystem.CanonicalPath(Path.GetFullPath(request.LocalPath));
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(token) : null;
        await PluginLifecycleLease.LockConnectionAsync(db, connectionId, token);
        await using var rootBoundary = await LibraryRootConfigurationLease.AcquireAsync(db, token);
        var connection = await db.IntegrationConnections.AsNoTracking().SingleOrDefaultAsync(row => row.Id == connectionId, token)
            ?? throw new ConnectionNotFoundException();
        if (connection.Revision != expectedRevision) throw new ConnectionConflictException();
        var configured = await ListAsync(connectionId, token);
        var existing = configured.FirstOrDefault(mount => mount.RemoteRootId == request.RemoteRootId);
        if (existing is not null) {
            if (existing.LocalPath != local || existing.RemotePath != request.ExpectedRemotePath)
                throw new ArgumentException("This remote root already has an immutable mapping. Use its existing library.");
            return existing;
        }
        var remote = ExternalLibraryPaths.NormalizeRemote(request.ExpectedRemotePath);
        if (configured.Any(mount => {
            var other = ExternalLibraryPaths.NormalizeRemote(mount.RemotePath);
            // Conservative comparison prevents ambiguous Windows and network-share mappings on Unix hosts too.
            return remote.StartsWith(other, StringComparison.OrdinalIgnoreCase) || other.StartsWith(remote, StringComparison.OrdinalIgnoreCase);
        })) throw new ArgumentException("This remote folder overlaps an existing mapping for this connection.");
        await RequireDedicatedPathAsync(local, token);
        // Listing the folder proves access without creating a sentinel or altering external bytes.
        try { using var entries = Directory.EnumerateFileSystemEntries(local).GetEnumerator(); _ = entries.MoveNext(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { throw new ArgumentException("The local library folder cannot be read."); }
        var now = DateTimeOffset.UtcNow;
        var root = new LibraryRootRow { Id = Guid.NewGuid(), Path = local, Label = request.Label.Trim(), Enabled = false, Recursive = true,
            ScanVideos = request.EntityKind is EntityKind.Movie or EntityKind.VideoSeries, ScanBooks = request.EntityKind is EntityKind.Book or EntityKind.ComicSeries,
            ScanImages = request.EntityKind is EntityKind.Image or EntityKind.Gallery, ScanAudio = request.EntityKind is EntityKind.AudioLibrary,
            AutoIdentify = false, IsNsfw = request.IsNsfw, CreatedAt = now, UpdatedAt = now };
        if (!root.ScanVideos && !root.ScanBooks && !root.ScanImages && !root.ScanAudio) throw new ArgumentException("This media kind does not yet support mapped library scans.");
        var mount = new ExternalLibraryMountRow { Id = Guid.NewGuid(), ConnectionId = connectionId, LibraryRootId = root.Id,
            RemoteRootId = request.RemoteRootId, RemotePath = request.ExpectedRemotePath, LocalPath = local, CreatedAt = now };
        db.LibraryRoots.Add(root); db.ExternalLibraryMounts.Add(mount);
        await db.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        cache.InvalidateRoots();
        return new(mount.Id, connectionId, root.Id, mount.RemoteRootId, mount.RemotePath, local, root.Label);
    }

    private async Task RequireDedicatedPathAsync(string local, CancellationToken token) {
        var reserved = new List<string?> { storage.DataPath, storage.CachePath };
        reserved.AddRange(await db.LibraryRoots.Select(root => root.Path).ToArrayAsync(token));
        reserved.AddRange(await db.ExternalLibraryMounts.Select(mount => mount.LocalPath).ToArrayAsync(token));
        reserved.AddRange(await db.DownloadClientConfigs.Select(client => client.DownloadDirectory).ToArrayAsync(token));
        reserved.AddRange(await db.RemotePathMappings.Select(mapping => mapping.LocalPath).ToArrayAsync(token));
        reserved.AddRange(await db.DownloadTransfers.Select(transfer => transfer.ContentPath).ToArrayAsync(token));
        reserved.AddRange(await db.DownloadTransfers.Select(transfer => transfer.SavePath).ToArrayAsync(token));
        reserved.AddRange(await db.Acquisitions.Select(acquisition => acquisition.FinalSourcePath).ToArrayAsync(token));
        reserved.AddRange(await db.DetachedDownloadCleanups.Select(cleanup => cleanup.ContentPath).ToArrayAsync(token));
        reserved.Add((await new SettingsService(new EfSettingsPersistence(db)).GetRecycleBinSettingsAsync(token)).Path);
        if (reserved.Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)).Any(path =>
            CompletedPayloadFileSystem.Overlaps(local, CompletedPayloadFileSystem.CanonicalPath(path!))))
            throw new ArgumentException("Use a dedicated folder outside existing libraries, downloads, recycle folders, and Prismedia data or cache.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MappedLibraryFile>> InspectAsync(Guid connectionId, IReadOnlyList<ManagedLibraryFile> files, CancellationToken token) {
        var mounts = await ListAsync(connectionId, token);
        var evidence = new List<MappedLibraryFile>(files.Count);
        foreach (var file in files) {
            token.ThrowIfCancellationRequested();
            ExternalLibraryMount? matched = null; string? local = null;
            try {
                foreach (var mount in mounts) {
                    var candidate = ExternalLibraryPaths.Resolve(mount.RemotePath, mount.LocalPath, file.Path);
                    if (candidate is null) continue;
                    if (matched is not null) throw new ArgumentException("More than one mapping matches this remote file.");
                    matched = mount; local = candidate;
                }
                if (matched is null) { evidence.Add(new(file.RemoteId, null, null, false, false, "No local mapping covers this remote file.")); continue; }
                await using var stream = new FileStream(local!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var sameSize = stream.Length == file.SizeBytes;
                evidence.Add(new(file.RemoteId, matched.LibraryRootId, local, true, sameSize, sameSize ? null : "The local file size differs from the manager's report."));
            } catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) {
                evidence.Add(new(file.RemoteId, matched?.LibraryRootId, local, false, false,
                    error is ArgumentException ? "The remote path cannot be mapped safely." : "The mapped file is missing or cannot be read."));
            }
        }
        return evidence;
    }
}
