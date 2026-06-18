using Microsoft.Extensions.Logging;
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
    IScanSnapshotStore? snapshots = null) : IJobHandler {
    public abstract JobType Type { get; }

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken) {
        if (!ScanRootPayload.TryParse(context.Job.PayloadJson, out var payload)) {
            var enabledRoots = await roots.GetEnabledRootsAsync(cancellationToken);
            var eligible = enabledRoots.Where(IsEligibleRoot).ToList();
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
                    await ScanRootWithSnapshotAsync(context, currentRoot, cancellationToken);
                    await roots.UpdateRootLastScannedAsync(currentRoot.Id, cancellationToken);
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
    }

    /// <summary>
    /// Runs the detailed scan for one root unless the incremental fast path determines nothing
    /// changed since the last scan, in which case the detailed pass is skipped.
    /// </summary>
    private async Task ScanRootWithSnapshotAsync(
        JobContext context, LibraryRootData root, CancellationToken cancellationToken) {
        if (snapshots is null) {
            // No snapshot store wired (e.g. in unit tests): always run the full scan.
            await ScanRootCoreAsync(context, root, cancellationToken);
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
            return;
        }

        if (delta.HasChanges && previous.Count > 0) {
            logger.LogInformation(
                "{JobType}: {Label} changed since last scan (+{Added} -{Removed} ~{Changed}), rescanning",
                scanKind, root.Label, delta.Added.Count, delta.Removed.Count, delta.Changed.Count);
        }

        await ScanRootCoreAsync(context, root, cancellationToken);
        await snapshots.ApplyAsync(root.Id, scanKind, delta, cancellationToken);
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

        var byPath = new Dictionary<string, FileSignature>(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>Discovers files, creates/updates entities, and enqueues downstream jobs for one root.</summary>
    protected abstract Task ScanRootCoreAsync(JobContext context, LibraryRootData root, CancellationToken cancellationToken);

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
