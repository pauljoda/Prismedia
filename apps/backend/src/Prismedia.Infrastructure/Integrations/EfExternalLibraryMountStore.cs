using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Files;
using Prismedia.Application.Integrations;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;
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
    public async Task<IReadOnlySet<Guid>> ListMountedLibraryRootIdsAsync(CancellationToken token) =>
        (await db.ExternalLibraryMounts.AsNoTracking()
            .Select(mount => mount.LibraryRootId)
            .ToArrayAsync(token))
        .ToHashSet();

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
        _ = RemoteLibraryPath.Parse(request.ExpectedRemotePath);
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
        var remote = RemoteLibraryPath.Parse(request.ExpectedRemotePath);
        if (configured.Any(mount => remote.Overlaps(RemoteLibraryPath.Parse(mount.RemotePath)))) {
            throw new ArgumentException("This remote folder overlaps an existing mapping for this connection.");
        }
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

    /// <inheritdoc />
    public async Task<ExternalLibraryMount> AttachAsync(Guid connectionId, long expectedRevision,
        AttachExistingExternalLibraryMountRequest request, CancellationToken token) =>
        (await AttachWithResultAsync(connectionId, expectedRevision, request, token)).Mount;

    /// <inheritdoc />
    public async Task<ExternalLibraryMountAttachment> AttachWithResultAsync(Guid connectionId, long expectedRevision,
        AttachExistingExternalLibraryMountRequest request, CancellationToken token) {
        if (string.IsNullOrWhiteSpace(request.ExpectedLocalPath) || request.ExpectedLocalPath.Length > 8192
            || request.ExpectedLocalPath.Any(char.IsControl) || !Path.IsPathFullyQualified(request.ExpectedLocalPath)
            || !Directory.Exists(request.ExpectedLocalPath))
            throw new ArgumentException("Choose an existing absolute local library folder that Prismedia can read.");
        var remote = RemoteLibraryPath.Parse(request.ExpectedRemotePath);
        var local = CompletedPayloadFileSystem.CanonicalPath(Path.GetFullPath(request.ExpectedLocalPath));
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(token) : null;
        await PluginLifecycleLease.LockConnectionAsync(db, connectionId, token);
        await using var rootBoundary = await LibraryRootConfigurationLease.AcquireAsync(db, token);
        var connection = await db.IntegrationConnections.AsNoTracking().SingleOrDefaultAsync(row => row.Id == connectionId, token)
            ?? throw new ConnectionNotFoundException();
        if (connection.Revision != expectedRevision) throw new ConnectionConflictException();

        var configured = await ListAsync(connectionId, token);
        var existing = configured.FirstOrDefault(mount => mount.RemoteRootId == request.RemoteRootId);
        if (existing is not null) {
            var sameLocalPath = FileSystemPathComparison.Comparer.Equals(
                CompletedPayloadFileSystem.CanonicalPath(existing.LocalPath), local);
            if (existing.LibraryRootId != request.ExistingLibraryRootId || existing.RemotePath != request.ExpectedRemotePath || !sameLocalPath)
                throw new ConnectionConflictException("This remote root already has an immutable mapping. Use its existing library.");
            return new(existing, Created: false);
        }
        if (configured.Any(mount => remote.Overlaps(RemoteLibraryPath.Parse(mount.RemotePath)))) {
            throw new ArgumentException("This remote folder overlaps an existing mapping for this connection.");
        }

        var root = await db.LibraryRoots.SingleOrDefaultAsync(row => row.Id == request.ExistingLibraryRootId, token)
            ?? throw new ArgumentException("The selected local library no longer exists. Refresh the library choices.");
        if (!Path.IsPathFullyQualified(root.Path) || !Directory.Exists(root.Path))
            throw new ArgumentException("The selected local library folder no longer exists or is not an absolute path.");
        var rootPath = CompletedPayloadFileSystem.CanonicalPath(Path.GetFullPath(root.Path));
        if (!FileSystemPathComparison.Comparer.Equals(rootPath, local))
            throw new ArgumentException("The selected local library path changed. Refresh the library choices before attaching it.");
        if (!Supports(root, request.EntityKind))
            throw new ArgumentException("The selected local library does not scan this media kind.");
        if (await db.ExternalLibraryMounts.AnyAsync(mount => mount.LibraryRootId == root.Id, token))
            throw new ArgumentException("The selected local library is already managed by another external mapping.");

        await RequireAdoptablePathAsync(root.Id, local, token);
        await RequireNoNativeWorkAsync(root.Id, token);
        try { using var entries = Directory.EnumerateFileSystemEntries(local).GetEnumerator(); _ = entries.MoveNext(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            throw new ArgumentException("The selected local library folder cannot be read.");
        }

        var mount = new ExternalLibraryMountRow { Id = Guid.NewGuid(), ConnectionId = connectionId, LibraryRootId = root.Id,
            RemoteRootId = request.RemoteRootId, RemotePath = request.ExpectedRemotePath, LocalPath = local, CreatedAt = DateTimeOffset.UtcNow };
        db.ExternalLibraryMounts.Add(mount);
        await db.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        cache.InvalidateRoots();
        return new(new(mount.Id, connectionId, root.Id, mount.RemoteRootId, mount.RemotePath, local, root.Label), Created: true);
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

    private async Task RequireAdoptablePathAsync(Guid rootId, string local, CancellationToken token) {
        var reserved = new List<string?> { storage.DataPath, storage.CachePath };
        reserved.AddRange(await db.LibraryRoots.Where(root => root.Id != rootId).Select(root => root.Path).ToArrayAsync(token));
        reserved.AddRange(await db.ExternalLibraryMounts.Select(mount => mount.LocalPath).ToArrayAsync(token));
        reserved.AddRange(await db.DownloadClientConfigs.Select(client => client.DownloadDirectory).ToArrayAsync(token));
        reserved.AddRange(await db.RemotePathMappings.Select(mapping => mapping.LocalPath).ToArrayAsync(token));
        reserved.AddRange(await db.DownloadTransfers.Select(transfer => transfer.ContentPath).ToArrayAsync(token));
        reserved.AddRange(await db.DownloadTransfers.Select(transfer => transfer.SavePath).ToArrayAsync(token));
        var terminal = new[] { AcquisitionStatus.Imported, AcquisitionStatus.Cancelled, AcquisitionStatus.Failed };
        reserved.AddRange(await db.Acquisitions.Where(acquisition => !terminal.Contains(acquisition.Status))
            .Select(acquisition => acquisition.FinalSourcePath).ToArrayAsync(token));
        reserved.AddRange(await db.DetachedDownloadCleanups.Select(cleanup => cleanup.ContentPath).ToArrayAsync(token));
        reserved.Add((await new SettingsService(new EfSettingsPersistence(db)).GetRecycleBinSettingsAsync(token)).Path);
        if (reserved.Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)).Any(path =>
            CompletedPayloadFileSystem.Overlaps(local, CompletedPayloadFileSystem.CanonicalPath(path!))))
            throw new ArgumentException("The selected library overlaps another library, download folder, recycle folder, or Prismedia data or cache.");
    }

    private async Task RequireNoNativeWorkAsync(Guid rootId, CancellationToken token) {
        var terminal = new[] { AcquisitionStatus.Imported, AcquisitionStatus.Cancelled, AcquisitionStatus.Failed };
        if (await db.Acquisitions.AnyAsync(acquisition => acquisition.TargetLibraryRootId == rootId && !terminal.Contains(acquisition.Status), token))
            throw new ArgumentException("The selected library has an active native acquisition. Finish or cancel it before attaching an external manager.");
        if (await db.Monitors.AnyAsync(monitor => monitor.TargetLibraryRootId == rootId && monitor.Status != MonitorStatus.Fulfilled, token))
            throw new ArgumentException("The selected library has a native monitor that can still acquire files. Remove it before attaching an external manager.");
    }

    private static bool Supports(LibraryRootRow root, EntityKind kind) => kind switch {
        EntityKind.Movie or EntityKind.VideoSeries => root.ScanVideos,
        EntityKind.Book or EntityKind.ComicSeries => root.ScanBooks,
        EntityKind.Image or EntityKind.Gallery => root.ScanImages,
        EntityKind.AudioLibrary => root.ScanAudio,
        _ => false
    };

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
