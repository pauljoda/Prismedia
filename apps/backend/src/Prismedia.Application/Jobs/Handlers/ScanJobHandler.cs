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
/// metadata sidecar is detected only when the concrete handler includes that sidecar category in
/// <see cref="SnapshotCategories"/>. Entity-producing categories remain listed by
/// <see cref="ScanCategories"/>.
/// </para>
/// </summary>
public abstract class ScanJobHandler(
    ILogger logger,
    IFileDiscovery fileDiscovery,
    ILibraryScanRootPersistence roots,
    IScanSnapshotStore? snapshots = null,
    IMediaProcessingStatePersistence? processingState = null,
    ILibraryFileChangeIntake? changeIntake = null) : IJobHandler {
    private const int ChangeBatchSize = 256;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        var timer = new JobPhaseTimer();
        var rootFailures = 0;
        string? firstRootError = null;
        var scannedRoots = 0;
        // Library-wide cleanups (outside-root removal, orphan pruning) run on deep integrity
        // scans and legacy all-roots scans only. Routine and change-driven scans stay scoped to
        // their delta; targeted mutation paths handle their own orphan checks inline.
        var runLibraryWideCleanup = true;

        if (!ScanRootPayload.TryParse(context.Job.PayloadJson, out var payload)) {
            IReadOnlyList<LibraryRootData> enabledRoots;
            using (timer.Phase("roots-list")) {
                enabledRoots = await roots.GetEnabledRootsAsync(cancellationToken);
            }
            var eligible = enabledRoots.Where(IsEligibleRoot).ToList();
            scannedRoots = eligible.Count;
            logger.LogInformation("{JobType}: scanning {Count} eligible roots", context.Job.Type.ToCode(), eligible.Count);

            for (var i = 0; i < eligible.Count; i++) {
                var listedRoot = eligible[i];
                LibraryRootData? currentRoot;
                using (timer.Phase("root-refresh")) {
                    currentRoot = await roots.GetLibraryRootAsync(listedRoot.Id, cancellationToken);
                }
                if (currentRoot is null) {
                    logger.LogInformation(
                        "{JobType}: skipping library root {RootId} because it no longer exists",
                        context.Job.Type.ToCode(), listedRoot.Id);
                } else if (!currentRoot.Enabled || !IsEligibleRoot(currentRoot)) {
                    logger.LogInformation(
                        "{JobType}: skipping library root {RootId} because it is no longer enabled for this scan",
                        context.Job.Type.ToCode(), listedRoot.Id);
                } else {
                    // One broken library must not freeze the others: record the failure, keep
                    // scanning the remaining roots, and fail the job at the end.
                    try {
                        using (timer.Phase("root-scan")) {
                            await ScanRootWithSnapshotAsync(context, currentRoot, changesOnly: false, cancellationToken);
                        }
                        using (timer.Phase("root-last-scanned")) {
                            await roots.UpdateRootLastScannedAsync(currentRoot.Id, cancellationToken);
                        }
                    } catch (OperationCanceledException) {
                        throw;
                    } catch (Exception ex) {
                        logger.LogError(ex, "{JobType}: scanning library root {RootId} failed", context.Job.Type.ToCode(), currentRoot.Id);
                        rootFailures++;
                        firstRootError ??= ex.Message;
                    }
                }

                // Never name the individual root here: the all-roots scan job is not scoped to a
                // single (potentially NSFW) target, so it is not redacted by the jobs list, and this
                // message is persisted and shown to every client regardless of their SFW mode.
                // A count keeps progress useful without leaking library names.
                using (timer.Phase("progress-write")) {
                    await context.ReportProgressAsync((i + 1) * 100 / eligible.Count,
                        $"Scanned {i + 1} of {eligible.Count} {(eligible.Count == 1 ? "library" : "libraries")}",
                        cancellationToken);
                }
            }
        } else {
            LibraryRootData? root;
            using (timer.Phase("root-load")) {
                root = await roots.GetLibraryRootAsync(payload.RootId, cancellationToken);
            }
            if (root is null) {
                logger.LogWarning("{JobType}: root {RootId} not found", context.Job.Type.ToCode(), payload.RootId);
                LogJobMetrics(context.Job.Type, scannedRoots, rootFailures, timer.Finish());
                return;
            }

            scannedRoots = 1;
            runLibraryWideCleanup = payload.Deep;
            using (timer.Phase("root-scan")) {
                await ScanRootWithSnapshotAsync(context, root, payload.ChangesOnly, cancellationToken);
            }
            using (timer.Phase("root-last-scanned")) {
                await roots.UpdateRootLastScannedAsync(root.Id, cancellationToken);
            }
            using (timer.Phase("progress-write")) {
                await context.ReportProgressAsync(100, $"Scanned {root.Label}", cancellationToken);
            }
        }

        // Library-wide cleanup previously ran after every scan job — including no-op rescans —
        // which made even a single touched file pay several whole-table sweeps. It now runs only
        // on deep integrity scans (and legacy all-roots scans), where a full pass is the point.
        if (runLibraryWideCleanup) {
            using (timer.Phase("cleanup-outside-roots")) {
                await RemoveEntitiesOutsideConfiguredRootsAsync(context.Job.Type, cancellationToken);
            }
            using (timer.Phase("cleanup-orphan-tags")) {
                await RemoveOrphanTagsIfEnabledAsync(context.Job.Type, cancellationToken);
            }
        }

        LogJobMetrics(context.Job.Type, scannedRoots, rootFailures, timer.Finish());

        if (rootFailures > 0) {
            throw new InvalidOperationException(
                $"{rootFailures} of {scannedRoots} libraries failed to scan (the rest completed). First error: {firstRootError}");
        }
    }

    private void LogJobMetrics(JobType jobType, int scannedRoots, int rootFailures, JobTimingReport report) {
        logger.LogInformation(
            "[METRICS] scan-job {JobType} — roots={RootCount} failures={FailureCount} — {Timing}",
            jobType.ToCode(),
            scannedRoots,
            rootFailures,
            report.ToLogString());
    }

    /// <summary>
    /// Runs the detailed scan for one root unless the incremental fast path determines nothing
    /// changed since the last scan, in which case the detailed pass is skipped.
    /// </summary>
    private async Task ScanRootWithSnapshotAsync(
        JobContext context,
        LibraryRootData root,
        bool changesOnly,
        CancellationToken cancellationToken) {
        var timer = new JobPhaseTimer();
        var mode = "full";
        var currentCount = 0;
        var previousCount = 0;
        var delta = ScanDelta.Empty;

        try {
            // Media-specific handlers can use this scope to serialize the signature snapshot and detailed
            // reconciliation with import-time filesystem changes. It deliberately covers the fast path too:
            // taking a pre-import snapshot and then scanning post-import files would advance inconsistent state.
            IAsyncDisposable? scanScope;
            using (timer.Phase("scan-gate-wait")) {
                scanScope = await EnterScanScopeAsync(root, cancellationToken);
            }
            await using var acquiredScanScope = scanScope;

            if (snapshots is null) {
                // No snapshot store wired (e.g. in unit tests): always run the full scan.
                mode = "full-no-snapshot-store";
                ScanRootOutcome outcome;
                using (timer.Phase("detailed-reconcile")) {
                    outcome = await ScanRootCoreAsync(context, root, cancellationToken);
                }
                ThrowIfFilesFailed(outcome);
                LogRootMetrics(context.Job.Type, root, mode, currentCount, previousCount, delta, timer.Finish());
                return;
            }

            var scanKind = context.Job.Type.ToCode();
            var pendingChanges = changeIntake is null
                ? LibraryFileChangeBatch.Empty
                : await changeIntake.LoadAsync(
                    root.Id,
                    scanKind,
                    ChangeBatchSize,
                    cancellationToken);
            if (changesOnly && pendingChanges.IsEmpty) {
                mode = "changes-empty";
                LogRootMetrics(context.Job.Type, root, mode, currentCount, previousCount, delta, timer.Finish());
                return;
            }

            IReadOnlySet<string> excluded;
            using (timer.Phase("excluded-paths")) {
                excluded = await roots.GetExcludedPathsForRootAsync(root.Id, cancellationToken);
            }

            IReadOnlyList<FileSignature> previous;
            using (timer.Phase("snapshot-load")) {
                previous = await snapshots.LoadAsync(root.Id, scanKind, cancellationToken);
            }
            previousCount = previous.Count;

            IReadOnlyList<FileSignature> current;
            if (changesOnly && previous.Count > 0) {
                using (timer.Phase("signature-scope-enumerate")) {
                    delta = await ComputeScopedDeltaAsync(
                        root,
                        excluded,
                        previous,
                        pendingChanges.Paths,
                        cancellationToken);
                }
                current = ApplyDelta(previous, delta);
                mode = "surgical-change-intake";
            } else {
                using (timer.Phase("signature-enumerate")) {
                    current = await EnumerateSignaturesAsync(root, excluded, cancellationToken);
                }
                using (timer.Phase("snapshot-diff")) {
                    delta = ScanSnapshotDiff.Compute(previous, current);
                }
            }
            currentCount = current.Count;

            // A snapshot exists and nothing on disk changed since it was taken, so the entities,
            // structure, and assets this scan would produce are already persisted. The first scan (no
            // snapshot) and any add/remove/change fall through to the full scan, which always sees the
            // whole file set and therefore keeps folder-context classification correct.
            if (previous.Count > 0 && !delta.HasChanges) {
                mode = changesOnly ? "changes-noop" : "unchanged";
                logger.LogInformation(
                    "{JobType}: no file changes in {Label} ({Count} files), skipping detailed scan",
                    scanKind, root.Label, current.Count);
                if (!changesOnly) {
                    // Full scans remain an integrity fallback for bounded catalog repairs that cannot be
                    // inferred from file signatures. Concrete handlers must keep this path surgical: it
                    // may repair proven drift, but must never requeue all unfinished enrichment.
                    using (timer.Phase("integrity-repair")) {
                        await OnUnchangedIntegrityScanAsync(context, root, cancellationToken);
                    }
                }
                await CompletePendingChangesAsync(root.Id, scanKind, pendingChanges, cancellationToken);
                LogRootMetrics(context.Job.Type, root, mode, currentCount, previousCount, delta, timer.Finish());
                return;
            }

            mode = previous.Count == 0
                ? "full-no-snapshot"
                : changesOnly
                    ? "surgical-change-intake"
                    : "full-changed";
            if (delta.HasChanges && previous.Count > 0) {
                logger.LogInformation(
                    "{JobType}: {Label} changed since last scan (+{Added} -{Removed} ~{Changed}), rescanning",
                    scanKind, root.Label, delta.Added.Count, delta.Removed.Count, delta.Changed.Count);
            }

            // A file whose on-disk signature changed may have been repaired or replaced, so any
            // unreadable-source (probe-failure) marker it carries is stale: clear it before the scan's
            // downstream checks so the file gets a fresh probing chance.
            if (processingState is not null && (delta.Changed.Count > 0 || delta.Added.Count > 0)) {
                using (timer.Phase("reset-changed-state")) {
                    var touchedPaths = delta.Changed.Concat(delta.Added).Select(signature => signature.Path).ToList();
                    await processingState.ClearProbeFailuresForPathsAsync(touchedPaths, cancellationToken);
                    await processingState.ClearManagedSubtitleCompletionForPathsAsync(touchedPaths, cancellationToken);
                }
            }

            if (delta.Changed.Count > 0) {
                using (timer.Phase("invalidate-replaced-sources")) {
                    await OnChangedFileSignaturesAsync(
                        delta.Changed.Select(signature => signature.Path).ToArray(),
                        cancellationToken);
                }
            }

            ScanRootOutcome detailedOutcome;
            using (timer.Phase("detailed-reconcile")) {
                detailedOutcome = previous.Count == 0
                    ? await ScanRootCoreAsync(context, root, cancellationToken)
                    : await ScanRootDeltaAsync(context, root, current, delta, cancellationToken);
            }

            // Files the scan could not persist are withheld from the snapshot so the next scan sees
            // them as still added/changed and retries exactly them; everything that succeeded advances
            // normally. The job still fails below so the skipped files stay visible.
            using (timer.Phase("snapshot-apply")) {
                await snapshots.ApplyAsync(
                    root.Id,
                    scanKind,
                    WithoutFailedPaths(delta, detailedOutcome),
                    cancellationToken);
            }
            ThrowIfFilesFailed(detailedOutcome);
            await CompletePendingChangesAsync(root.Id, scanKind, pendingChanges, cancellationToken);
            LogRootMetrics(context.Job.Type, root, mode, currentCount, previousCount, delta, timer.Finish());
        } catch (OperationCanceledException) {
            LogRootMetrics(context.Job.Type, root, "cancelled", currentCount, previousCount, delta, timer.Finish());
            throw;
        } catch (Exception) {
            LogRootMetrics(context.Job.Type, root, $"{mode}-failed", currentCount, previousCount, delta, timer.Finish());
            throw;
        }
    }

    /// <summary>
    /// Performs a bounded catalog-only integrity repair during an unchanged full scan. The default is
    /// idle; overrides may act only on proven persistence drift and queue work for the repaired Entities.
    /// Filesystem change scans never call this hook.
    /// </summary>
    protected virtual Task OnUnchangedIntegrityScanAsync(
        JobContext context,
        LibraryRootData root,
        CancellationToken cancellationToken) => Task.CompletedTask;

    private void LogRootMetrics(
        JobType jobType,
        LibraryRootData root,
        string mode,
        int currentCount,
        int previousCount,
        ScanDelta delta,
        JobTimingReport report) {
        logger.LogInformation(
            "[METRICS] scan-root {JobType} {Label} — mode={Mode} current={CurrentCount} previous={PreviousCount} +{Added} -{Removed} ~{Changed} — {Timing}",
            jobType.ToCode(),
            root.Label,
            mode,
            currentCount,
            previousCount,
            delta.Added.Count,
            delta.Removed.Count,
            delta.Changed.Count,
            report.ToLogString());
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
        var categories = SnapshotCategories;
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

    private async Task<ScanDelta> ComputeScopedDeltaAsync(
        LibraryRootData root,
        IReadOnlySet<string> excluded,
        IReadOnlyList<FileSignature> previous,
        IReadOnlyList<string> changedPaths,
        CancellationToken cancellationToken) {
        var previousByPath = previous.ToDictionary(
            signature => signature.Path,
            FileSystemPathComparison.Comparer);
        var scopedPrevious = new Dictionary<string, FileSignature>(FileSystemPathComparison.Comparer);
        var scopedCurrent = new Dictionary<string, FileSignature>(FileSystemPathComparison.Comparer);

        foreach (var rawPath in changedPaths) {
            var path = Path.GetFullPath(rawPath);
            foreach (var signature in previousByPath.Values.Where(signature =>
                         FileSystemPathComparison.IsSameOrDescendant(path, signature.Path))) {
                scopedPrevious[signature.Path] = signature;
            }

            if (Directory.Exists(path)) {
                if (!root.Recursive
                    && !FileSystemPathComparison.Comparer.Equals(
                        Path.TrimEndingDirectorySeparator(root.Path),
                        Path.TrimEndingDirectorySeparator(path))) {
                    continue;
                }

                foreach (var signature in await EnumerateSignaturesForPathAsync(
                             path,
                             root.Recursive,
                             excluded,
                             cancellationToken)) {
                    scopedCurrent[signature.Path] = signature;
                }
                continue;
            }

            if (!File.Exists(path) || Path.GetDirectoryName(path) is not { } parent) {
                continue;
            }
            foreach (var signature in await EnumerateSignaturesForPathAsync(
                         parent,
                         recursive: false,
                         excluded,
                         cancellationToken)) {
                if (FileSystemPathComparison.Comparer.Equals(signature.Path, path)) {
                    scopedCurrent[signature.Path] = signature;
                }
            }
        }

        return ScanSnapshotDiff.Compute(scopedPrevious.Values.ToArray(), scopedCurrent.Values.ToArray());
    }

    private async Task<IReadOnlyList<FileSignature>> EnumerateSignaturesForPathAsync(
        string path,
        bool recursive,
        IReadOnlySet<string> excluded,
        CancellationToken cancellationToken) {
        var byPath = new Dictionary<string, FileSignature>(FileSystemPathComparison.Comparer);
        foreach (var category in SnapshotCategories) {
            foreach (var signature in await fileDiscovery.DiscoverFileSignaturesAsync(
                         path,
                         category,
                         recursive,
                         excluded,
                         cancellationToken)) {
                byPath[signature.Path] = signature;
            }
        }
        return byPath.Values.ToArray();
    }

    private static IReadOnlyList<FileSignature> ApplyDelta(
        IReadOnlyList<FileSignature> previous,
        ScanDelta delta) {
        var result = previous.ToDictionary(
            signature => signature.Path,
            FileSystemPathComparison.Comparer);
        foreach (var removed in delta.Removed) result.Remove(removed.Path);
        foreach (var changed in delta.Changed) result[changed.Path] = changed;
        foreach (var added in delta.Added) result[added.Path] = added;
        return result.Values.ToArray();
    }

    private Task CompletePendingChangesAsync(
        Guid rootId,
        string scanKind,
        LibraryFileChangeBatch batch,
        CancellationToken cancellationToken) =>
        changeIntake is null || batch.IsEmpty
            ? Task.CompletedTask
            : changeIntake.CompleteAsync(
                rootId,
                scanKind,
                batch.Paths,
                batch.ObservedThrough,
                cancellationToken);

    /// <summary>Returns true if this root should be scanned by this handler's media type.</summary>
    protected abstract bool IsEligibleRoot(LibraryRootData root);

    /// <summary>
    /// The media categories this handler enumerates under a root. Drives the incremental snapshot, so
    /// it must list every category the handler's detailed scan discovers (for example comic archives
    /// and single-file books for the book scan).
    /// </summary>
    protected abstract IReadOnlyList<MediaCategory> ScanCategories { get; }

    /// <summary>
    /// File categories included in the incremental snapshot. Defaults to the categories that produce
    /// entities, but a handler may add files it consumes as sidecars so changing only a sidecar still
    /// enters the detailed reconciliation path.
    /// </summary>
    protected virtual IReadOnlyList<MediaCategory> SnapshotCategories => ScanCategories;

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
    /// Reconciles a known signature delta after the cheap root walk. Handlers that can safely preserve
    /// hierarchy from the complete signature set override this to touch only affected files or folders;
    /// other media families retain the full reconciliation fallback.
    /// </summary>
    protected virtual Task<ScanRootOutcome> ScanRootDeltaAsync(
        JobContext context,
        LibraryRootData root,
        IReadOnlyList<FileSignature> current,
        ScanDelta delta,
        CancellationToken cancellationToken) =>
        ScanRootCoreAsync(context, root, cancellationToken);

    /// <summary>
    /// Invalidates byte-derived state before changed source files are reconciled. Media handlers may
    /// ignore paths they do not own, such as metadata sidecars included only for snapshot detection.
    /// </summary>
    protected virtual Task OnChangedFileSignaturesAsync(
        IReadOnlyCollection<string> changedPaths,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Removes source-backed media that is no longer covered by any configured library root. This
    /// catches leftovers from older library-root deletions even when this scan's detailed per-root
    /// pass was skipped by the snapshot fast path.
    /// </summary>
    private async Task RemoveEntitiesOutsideConfiguredRootsAsync(JobType jobType, CancellationToken cancellationToken) {
        var removed = await roots.RemoveEntitiesOutsideLibraryRootsAsync(cancellationToken);
        if (removed > 0) {
            logger.LogInformation(
                "{JobType}: removed {Count} media entries outside configured library roots",
                jobType.ToCode(), removed);
        }
    }

    /// <summary>
    /// Deletes tags that nothing references when the "Remove orphan tags" setting is on. Runs once at
    /// the end of <em>every</em> scan job — video, audio, books, images — not just one kind, so any
    /// scan keeps the tag list tidy. A tag's last reference is usually dropped by untagging or
    /// deleting media, which changes no files, so this runs even when the incremental fast path
    /// skipped every root's detailed pass.
    /// </summary>
    private async Task RemoveOrphanTagsIfEnabledAsync(JobType jobType, CancellationToken cancellationToken) {
        var settings = await roots.GetSettingsAsync(cancellationToken);
        if (!settings.RemoveOrphanTags) {
            return;
        }

        var removed = await roots.RemoveOrphanTagsAsync(cancellationToken);
        if (removed > 0) {
            logger.LogInformation("{JobType}: removed {Count} orphan tags with no references", jobType.ToCode(), removed);
        }
    }

    /// <summary>File discovery port for subclass use.</summary>
    protected IFileDiscovery FileDiscovery => fileDiscovery;

    /// <summary>Root and scan-setting persistence port for subclass use.</summary>
    protected ILibraryScanRootPersistence Roots => roots;
}
