using Microsoft.Extensions.Logging;
using Prismedia.Application.Files;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Application.Jobs.Scanning;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs.Handlers;

/// <summary>
/// Base class for library scan handlers. Manages root ID parsing from the job payload,
/// root filtering by scan type, and the single-root vs. all-roots iteration pattern.
/// Subclasses implement <see cref="IsEligibleRoot"/>, <see cref="ScanCategories"/>, and
/// <see cref="ScanRootCoreAsync"/>.
/// <para>
/// Each per-root scan is wrapped with an incremental fast path: before doing the detailed work the
/// base enumerates the root's files with a cheap size/mtime signature and diffs them against the
/// snapshot the last scan stored. When nothing was added, removed, or changed the detailed pass is
/// skipped entirely; otherwise the full scan runs (so folder-context classification always sees the
/// complete file set) and the snapshot is updated. The first scan, or any scan with no snapshot store
/// wired, always runs the full pass.
/// </para>
/// <para>
/// Trade-off: because the signature only covers media files of the handler's categories, editing a
/// metadata sidecar (for example a <c>.nfo</c>) without changing the media file is not detected by a
/// no-change scan and is picked up on the next real change or via an explicit entity refresh.
/// </para>
/// </summary>
public abstract class ScanJobHandler(
    ILogger logger,
    IFileDiscovery fileDiscovery,
    ILibraryScanRootPersistence roots,
    IScanSnapshotStore? snapshots = null,
    IMediaProcessingStatePersistence? processingState = null) : IJobHandler {
    public abstract JobType Type { get; }

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var rootFailures = 0;
        string? firstRootError = null;
        var scannedRoots = 0;

        if (!ScanRootPayload.TryParse(context.Job.PayloadJson, out var payload)) {
            var enabledRoots = await roots.GetEnabledRootsAsync(cancellationToken);
            var eligible = enabledRoots.Where(IsEligibleRoot).ToList();
            scannedRoots = eligible.Count;
            logger.LogInformation("{JobType}: scanning {Count} eligible roots", Type.ToCode(), eligible.Count);

            for (var i = 0; i < eligible.Count; i++) {
                var listedRoot = eligible[i];
                var currentRoot = await roots.GetLibraryRootAsync(listedRoot.Id, cancellationToken);
                if (currentRoot is null) {
                    logger.LogInformation(
                        "{JobType}: skipping library root {RootId} because it no longer exists",
                        Type.ToCode(), listedRoot.Id);
                } else if (!currentRoot.Enabled || !IsEligibleRoot(currentRoot)) {
                    logger.LogInformation(
                        "{JobType}: skipping library root {RootId} because it is no longer enabled for this scan",
                        Type.ToCode(), listedRoot.Id);
                } else {
                    // One broken library must not freeze the others: record the failure, keep
                    // scanning the remaining roots, and fail the job at the end.
                    try {
                        await ScanRootWithSnapshotAsync(context, currentRoot, cancellationToken);
                        await roots.UpdateRootLastScannedAsync(currentRoot.Id, cancellationToken);
                    } catch (OperationCanceledException) {
                        throw;
                    } catch (Exception ex) {
                        logger.LogError(ex, "{JobType}: scanning library root {RootId} failed", Type.ToCode(), currentRoot.Id);
                        rootFailures++;
                        firstRootError ??= ex.Message;
                    }
                }

                // Never name the individual root here: the all-roots scan job is not scoped to a
                // single (potentially NSFW) target, so it is not redacted by the jobs list, and this
                // message is persisted and shown to every client regardless of their SFW mode.
                // A count keeps progress useful without leaking library names.
                await context.ReportProgressAsync((i + 1) * 100 / eligible.Count,
                    $"Scanned {i + 1} of {eligible.Count} {(eligible.Count == 1 ? "library" : "libraries")}",
                    cancellationToken);
            }
        } else {
            var root = await roots.GetLibraryRootAsync(payload.RootId, cancellationToken);
            if (root is null) {
                logger.LogWarning("{JobType}: root {RootId} not found", Type.ToCode(), payload.RootId);
                return;
            }

            await ScanRootWithSnapshotAsync(context, root, cancellationToken);
            await roots.UpdateRootLastScannedAsync(root.Id, cancellationToken);
            await context.ReportProgressAsync(100, $"Scanned {root.Label}", cancellationToken);
        }

        // Runs once per scan job after every root is processed — including when every root's detailed
        // pass was skipped by the incremental fast path — so global cleanup that does not depend on
        // file changes (e.g. deleted library roots and orphaned taxonomy) still happens on an
        // otherwise no-op rescan.
        await RemoveEntitiesOutsideConfiguredRootsAsync(cancellationToken);
        await RemoveOrphanTagsIfEnabledAsync(cancellationToken);

        if (rootFailures > 0) {
            throw new InvalidOperationException(
                $"{rootFailures} of {scannedRoots} libraries failed to scan (the rest completed). First error: {firstRootError}");
        }
    }

    /// <summary>
    /// Runs the detailed scan for one root unless the incremental fast path determines nothing
    /// changed since the last scan, in which case the detailed pass is skipped.
    /// </summary>
    private async Task ScanRootWithSnapshotAsync(
        JobContext context, LibraryRootData root, CancellationToken cancellationToken) {
        // Media-specific handlers can use this scope to serialize the signature snapshot and detailed
        // reconciliation with import-time filesystem changes. It deliberately covers the fast path too:
        // taking a pre-import snapshot and then scanning post-import files would advance inconsistent state.
        await using var scanScope = await EnterScanScopeAsync(root, cancellationToken);

        if (snapshots is null) {
            // No snapshot store wired (e.g. in unit tests): always run the full scan.
            ThrowIfFilesFailed(await ScanRootCoreAsync(context, root, cancellationToken));
            return;
        }

        var scanKind = Type.ToCode();
        var excluded = await roots.GetExcludedPathsForRootAsync(root.Id, cancellationToken);
        var current = await EnumerateSignaturesAsync(root, excluded, cancellationToken);
        var previous = await snapshots.LoadAsync(root.Id, scanKind, cancellationToken);
        var delta = ScanSnapshotDiff.Compute(previous, current);

        // A snapshot exists and nothing on disk changed since it was taken, so the entities,
        // structure, and assets this scan would produce are already persisted. The first scan (no
        // snapshot) and any add/remove/change fall through to the full scan, which always sees the
        // whole file set and therefore keeps folder-context classification correct.
        if (previous.Count > 0 && !delta.HasChanges) {
            logger.LogInformation(
                "{JobType}: no file changes in {Label} ({Count} files), skipping detailed scan",
                scanKind, root.Label, current.Count);
            await OnNoFileChangesAsync(context, root, cancellationToken);
            return;
        }

        if (delta.HasChanges && previous.Count > 0) {
            logger.LogInformation(
                "{JobType}: {Label} changed since last scan (+{Added} -{Removed} ~{Changed}), rescanning",
                scanKind, root.Label, delta.Added.Count, delta.Removed.Count, delta.Changed.Count);
        }

        // A file whose on-disk signature changed may have been repaired or replaced, so any
        // unreadable-source (probe-failure) marker it carries is stale: clear it before the scan's
        // downstream checks so the file gets a fresh probing chance.
        if (processingState is not null && (delta.Changed.Count > 0 || delta.Added.Count > 0)) {
            var touchedPaths = delta.Changed.Concat(delta.Added).Select(signature => signature.Path).ToList();
            await processingState.ClearProbeFailuresForPathsAsync(touchedPaths, cancellationToken);
        }

        var outcome = await ScanRootCoreAsync(context, root, cancellationToken);

        // Files the scan could not persist are withheld from the snapshot so the next scan sees
        // them as still added/changed and retries exactly them; everything that succeeded advances
        // normally. The job still fails below so the skipped files stay visible.
        await snapshots.ApplyAsync(root.Id, scanKind, WithoutFailedPaths(delta, outcome), cancellationToken);
        ThrowIfFilesFailed(outcome);
    }

    private static ScanDelta WithoutFailedPaths(ScanDelta delta, ScanRootOutcome outcome) {
        if (outcome.FailedPaths.Count == 0) return delta;

        var failed = new HashSet<string>(outcome.FailedPaths, FileSystemPathComparison.Comparer);
        return delta with {
            Added = delta.Added.Where(signature => !failed.Contains(signature.Path)).ToArray(),
            Changed = delta.Changed.Where(signature => !failed.Contains(signature.Path)).ToArray()
        };
    }

    private static void ThrowIfFilesFailed(ScanRootOutcome outcome) {
        if (outcome.FailedPaths.Count == 0) return;

        var sample = string.Join("; ", outcome.FailedPaths.Take(3));
        throw new InvalidOperationException(
            $"{outcome.FailedPaths.Count} file(s) could not be persisted and were skipped so the rest of the scan could finish: {sample}. They will be retried on the next scan.");
    }

    /// <summary>
    /// Enumerates the current file signatures across every media category this scan covers, combined
    /// into a single set keyed by path so a handler that scans more than one category (a book root
    /// scans comic archives and single-file books) keeps one snapshot of everything it processes.
    /// </summary>
    private async Task<IReadOnlyList<FileSignature>> EnumerateSignaturesAsync(
        LibraryRootData root, IReadOnlySet<string> excluded, CancellationToken cancellationToken) {
        var categories = ScanCategories;
        if (categories.Count == 1) {
            return await fileDiscovery.DiscoverFileSignaturesAsync(
                root.Path, categories[0], root.Recursive, excluded, cancellationToken);
        }

        var byPath = new Dictionary<string, FileSignature>(FileSystemPathComparison.Comparer);
        foreach (var category in categories) {
            var signatures = await fileDiscovery.DiscoverFileSignaturesAsync(
                root.Path, category, root.Recursive, excluded, cancellationToken);
            foreach (var signature in signatures) {
                byPath[signature.Path] = signature;
            }
        }

        return byPath.Values.ToArray();
    }

    /// <summary>Returns true if this root should be scanned by this handler's media type.</summary>
    protected abstract bool IsEligibleRoot(LibraryRootData root);

    /// <summary>
    /// The media categories this handler enumerates under a root. Drives the incremental snapshot, so
    /// it must list every category the handler's detailed scan discovers (for example comic archives
    /// and single-file books for the book scan).
    /// </summary>
    protected abstract IReadOnlyList<MediaCategory> ScanCategories { get; }

    /// <summary>
    /// Optionally enters a media-specific concurrency scope around one root's snapshot and detailed scan.
    /// Most scan kinds need no coordination; video overrides this to avoid racing TV import placement.
    /// </summary>
    protected virtual ValueTask<IAsyncDisposable?> EnterScanScopeAsync(
        LibraryRootData root, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IAsyncDisposable?>(null);

    /// <summary>
    /// Discovers files, creates/updates entities, and enqueues downstream jobs for one root.
    /// Returns which discovered files, if any, could not be persisted and were skipped; the base
    /// handler keeps those out of the scan snapshot and fails the job after the rest is saved.
    /// </summary>
    protected abstract Task<ScanRootOutcome> ScanRootCoreAsync(JobContext context, LibraryRootData root, CancellationToken cancellationToken);

    /// <summary>
    /// Runs when the incremental snapshot proves no media files changed and the detailed scan is
    /// skipped. Subclasses can enqueue cheap metadata-only follow-up work that does not require
    /// re-upserting the media tree.
    /// </summary>
    protected virtual Task OnNoFileChangesAsync(
        JobContext context, LibraryRootData root, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>
    /// Removes source-backed media that is no longer covered by any configured library root. This
    /// catches leftovers from older library-root deletions even when this scan's detailed per-root
    /// pass was skipped by the snapshot fast path.
    /// </summary>
    private async Task RemoveEntitiesOutsideConfiguredRootsAsync(CancellationToken cancellationToken) {
        var removed = await roots.RemoveEntitiesOutsideLibraryRootsAsync(cancellationToken);
        if (removed > 0) {
            logger.LogInformation(
                "{JobType}: removed {Count} media entries outside configured library roots",
                Type.ToCode(), removed);
        }
    }

    /// <summary>
    /// Deletes tags that nothing references when the "Remove orphan tags" setting is on. Runs once at
    /// the end of <em>every</em> scan job — video, audio, books, images — not just one kind, so any
    /// scan keeps the tag list tidy. A tag's last reference is usually dropped by untagging or
    /// deleting media, which changes no files, so this runs even when the incremental fast path
    /// skipped every root's detailed pass.
    /// </summary>
    private async Task RemoveOrphanTagsIfEnabledAsync(CancellationToken cancellationToken) {
        var settings = await roots.GetSettingsAsync(cancellationToken);
        if (!settings.RemoveOrphanTags) {
            return;
        }

        var removed = await roots.RemoveOrphanTagsAsync(cancellationToken);
        if (removed > 0) {
            logger.LogInformation("{JobType}: removed {Count} orphan tags with no references", Type.ToCode(), removed);
        }
    }

    /// <summary>File discovery port for subclass use.</summary>
    protected IFileDiscovery FileDiscovery => fileDiscovery;

    /// <summary>Root and scan-setting persistence port for subclass use.</summary>
    protected ILibraryScanRootPersistence Roots => roots;
}
