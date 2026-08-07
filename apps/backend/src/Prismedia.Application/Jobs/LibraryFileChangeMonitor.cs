using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Converts filesystem notifications into durable, coalesced path intents after a short quiet period.
/// The watcher is a latency hint rather than the source of truth: periodic full scans remain the
/// integrity fallback for unavailable mounts, watcher overflow, and filesystems without notifications.
/// </summary>
internal sealed class LibraryFileChangeMonitor(
    IServiceScopeFactory scopeFactory,
    ILogger<LibraryFileChangeMonitor> logger) : BackgroundService {
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RootRefreshInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PendingSweepInterval = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<(Guid RootId, string Path), DateTimeOffset> observations = new();
    private readonly Dictionary<Guid, WatchedRoot> watchers = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        logger.LogInformation("Library filesystem change monitor started.");
        var nextRootRefresh = DateTimeOffset.MinValue;
        var nextPendingSweep = DateTimeOffset.MinValue;
        try {
            while (!stoppingToken.IsCancellationRequested) {
                var now = DateTimeOffset.UtcNow;
                if (now >= nextRootRefresh) {
                    await RefreshWatchersAsync(stoppingToken);
                    nextRootRefresh = now.Add(RootRefreshInterval);
                }

                await FlushQuietObservationsAsync(now, stoppingToken);
                if (now >= nextPendingSweep) {
                    await QueueOutstandingIntentsAsync(stoppingToken);
                    nextPendingSweep = now.Add(PendingSweepInterval);
                }

                await Task.Delay(PollInterval, stoppingToken);
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            // Normal hosted-service shutdown.
        } finally {
            foreach (var watched in watchers.Values) {
                watched.Watcher.Dispose();
            }
            watchers.Clear();
        }
    }

    private async Task RefreshWatchersAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var roots = await scope.ServiceProvider.GetRequiredService<IFilesPersistence>()
            .ListRootsAsync(cancellationToken);
        var desired = roots.Where(root => root.Enabled).ToDictionary(root => root.Id);

        foreach (var staleId in watchers.Keys.Where(id => !desired.ContainsKey(id)).ToArray()) {
            watchers.Remove(staleId, out var stale);
            stale?.Watcher.Dispose();
        }

        foreach (var root in desired.Values) {
            var path = Path.GetFullPath(root.Path);
            if (watchers.TryGetValue(root.Id, out var existing)
                && FileSystemPathComparison.Comparer.Equals(existing.Root.Path, path)
                && existing.Root.Recursive == root.Recursive) {
                existing.Root = root with { Path = path };
                continue;
            }

            if (existing is not null) {
                existing.Watcher.Dispose();
                watchers.Remove(root.Id);
            }
            if (!Directory.Exists(path)) {
                continue;
            }

            try {
                var normalizedRoot = root with { Path = path };
                var watcher = new FileSystemWatcher(path) {
                    IncludeSubdirectories = root.Recursive,
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                    EnableRaisingEvents = false
                };
                watcher.Created += (_, args) => Observe(root.Id, args.FullPath);
                watcher.Changed += (_, args) => Observe(root.Id, args.FullPath);
                watcher.Deleted += (_, args) => Observe(root.Id, args.FullPath);
                watcher.Renamed += (_, args) => {
                    Observe(root.Id, args.OldFullPath);
                    Observe(root.Id, args.FullPath);
                };
                watcher.Error += (_, args) => {
                    logger.LogWarning(
                        args.GetException(),
                        "Filesystem watcher overflowed or failed for library root {RootId}; scheduling integrity reconciliation.",
                        root.Id);
                    Observe(root.Id, path);
                };
                watchers[root.Id] = new WatchedRoot(normalizedRoot, watcher);
                watcher.EnableRaisingEvents = true;
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                logger.LogWarning(
                    exception,
                    "Could not watch library root {RootId}; scheduled integrity scans remain active.",
                    root.Id);
            }
        }
    }

    private void Observe(Guid rootId, string path) {
        try {
            observations[(rootId, Path.GetFullPath(path))] = DateTimeOffset.UtcNow;
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) {
            logger.LogDebug(exception, "Ignored invalid filesystem notification path for root {RootId}.", rootId);
        }
    }

    private async Task FlushQuietObservationsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        var ready = observations
            .Where(pair => now - pair.Value >= QuietPeriod)
            .Select(pair => pair.Key)
            .ToArray();
        if (ready.Length == 0) {
            return;
        }

        foreach (var key in ready) {
            observations.TryRemove(key, out _);
        }
        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
        var intake = scope.ServiceProvider.GetRequiredService<ILibraryFileChangeIntake>();
        foreach (var group in ready.GroupBy(key => key.RootId)) {
            if (!watchers.TryGetValue(group.Key, out var watched)) {
                continue;
            }
            var rootPath = Path.TrimEndingDirectorySeparator(watched.Root.Path);
            var paths = group
                .Select(key => key.Path)
                .Where(path => FileSystemPathComparison.IsSameOrDescendant(rootPath, path))
                .Distinct(FileSystemPathComparison.Comparer)
                .ToArray();
            if (paths.Length == 0) {
                continue;
            }
            await LibraryScanJobs.QueueChangedPathsForRootAsync(
                queue,
                intake,
                watched.Root.Id,
                watched.Root.Label,
                Selection(watched.Root),
                paths,
                cancellationToken);
        }
    }

    private async Task QueueOutstandingIntentsAsync(CancellationToken cancellationToken) {
        if (watchers.Count == 0) {
            return;
        }
        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
        var intake = scope.ServiceProvider.GetRequiredService<ILibraryFileChangeIntake>();
        foreach (var watched in watchers.Values) {
            await LibraryScanJobs.QueuePendingChangesForRootAsync(
                queue,
                intake,
                watched.Root.Id,
                watched.Root.Label,
                Selection(watched.Root),
                cancellationToken);
        }
    }

    private static LibraryScanSelection Selection(FileLibraryRoot root) =>
        new(root.ScanVideos, root.ScanImages, root.ScanAudio, root.ScanBooks);

    private sealed class WatchedRoot(FileLibraryRoot root, FileSystemWatcher watcher) {
        public FileLibraryRoot Root { get; set; } = root;
        public FileSystemWatcher Watcher { get; } = watcher;
    }
}
