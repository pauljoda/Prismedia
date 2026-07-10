using Prismedia.Application.Files;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>What a merged import does with one incoming file, against the existing on-disk tree.</summary>
public enum MergeFileAction {
    /// <summary>No owned file claims this unit — place it at the target path.</summary>
    PlaceNew,

    /// <summary>The incoming file strictly upgrades the owned one — swap it in atomically.</summary>
    ReplaceUpgrade,

    /// <summary>The owned file is as good or better (or either side is unrankable) — the incoming file is discarded.</summary>
    DropNotUpgrade,

    /// <summary>A genuine upgrade that changes the file extension — never auto-swapped; surfaced for manual import.</summary>
    DropFormatChange,

    /// <summary>The physical-file coverage cannot be reconciled without splitting, merging, or losing episode owners.</summary>
    HoldStructuralConflict,
}

/// <summary>
/// One merged-import decision: the payload-relative source, the absolute target it maps to inside the
/// existing tree, the action, and — for a replace — the owned file being upgraded.
/// </summary>
public sealed record MergedImportItem(
    string SourceRelativePath,
    string TargetAbsolutePath,
    MergeFileAction Action,
    string? OwnedFilePath = null);

/// <summary>
/// Pure merge planning for a TV import whose acquisition links to a series that already lives on disk.
/// Folders anchor on the EXISTING tree — the season's real folder when the season exists, else a
/// template-named season folder inside the existing series folder; the user's layout is never renamed
/// or reorganized. Per-file collisions follow the upgrade vocabulary of
/// <see cref="MediaUpgradeSpecification"/>: strictly higher on the ladder replaces; equal quality
/// replaces only for a strictly higher PROPER/REPACK revision under
/// <see cref="ProperDownloadPolicy.PreferAndUpgrade"/>; anything unrankable on either side is dropped
/// (never overwrite what we can't judge); an upgrade that would change the file extension is refused
/// (a swapped extension orphans the entity's path) and surfaced for manual import.
/// </summary>
public static class TvExistingTargetMerge {
    /// <param name="units">The release's placeable units (from <see cref="TvImportPlanBuilder.PlanUnits"/>).</param>
    /// <param name="layout">The existing series' on-disk layout.</param>
    /// <param name="seasonSegment">Renders the season folder NAME for a season number the tree doesn't have yet.</param>
    /// <param name="incomingQualityPosition">The selected release's ladder ordinal (0 = unknown).</param>
    /// <param name="incomingRevision">The selected release's PROPER/REPACK revision (1 = plain).</param>
    /// <param name="properPolicy">The profile's proper policy — gates the equal-quality revision replace.</param>
    public static IReadOnlyList<MergedImportItem> Plan(
        IReadOnlyList<TvPlanUnit> units,
        TvSeriesDiskLayout layout,
        Func<int, string> seasonSegment,
        int incomingQualityPosition,
        int incomingRevision,
        ProperDownloadPolicy properPolicy,
        bool allowFormatChange = false) {
        var ownedPathBySlot = new Dictionary<(int Season, int Episode), string>();
        var ownedSlotsByPath = new Dictionary<string, HashSet<(int Season, int Episode)>>(FileSystemPathComparison.Comparer);
        foreach (var (seasonNumber, season) in layout.Seasons) {
            foreach (var (episodeNumber, path) in season.EpisodeFileByNumber) {
                var slot = (seasonNumber, episodeNumber);
                var fullPath = Path.GetFullPath(path);
                ownedPathBySlot[slot] = fullPath;
                if (!ownedSlotsByPath.TryGetValue(fullPath, out var slots)) {
                    slots = [];
                    ownedSlotsByPath.Add(fullPath, slots);
                }

                slots.Add(slot);
            }
        }

        var claimedSlotsByUnit = units
            .Select(unit => unit.ExtraEpisodes
                .Prepend(unit.Episode)
                .Select(episode => (unit.Season, Episode: episode))
                .ToHashSet())
            .ToArray();
        var occupiedPathsByUnit = claimedSlotsByUnit
            .Select(slots => slots
                .Where(ownedPathBySlot.ContainsKey)
                .Select(slot => ownedPathBySlot[slot])
                .ToHashSet(FileSystemPathComparison.Comparer))
            .ToArray();

        // One automatic replacement can reconcile exactly one incoming physical file with exactly one
        // existing physical file. The forward check catches a bundled incoming file spanning separate
        // owned files; this reverse count catches separate incoming files trying to replace one shared
        // multi-episode file.
        var incomingCountByOwnedPath = new Dictionary<string, int>(FileSystemPathComparison.Comparer);
        foreach (var occupiedPaths in occupiedPathsByUnit) {
            foreach (var path in occupiedPaths) {
                incomingCountByOwnedPath[path] = incomingCountByOwnedPath.GetValueOrDefault(path) + 1;
            }
        }

        var items = new List<MergedImportItem>(units.Count);
        for (var index = 0; index < units.Count; index++) {
            var unit = units[index];
            var claimedSlots = claimedSlotsByUnit[index];
            var occupiedPaths = occupiedPathsByUnit[index];
            var season = layout.Seasons.GetValueOrDefault(unit.Season);
            var seasonFolder = season?.FolderPath
                ?? Path.Combine(layout.SeriesFolderPath, seasonSegment(unit.Season));
            var desiredTarget = Path.Combine(seasonFolder, unit.FileName);

            if (occupiedPaths.Count > 1
                || occupiedPaths.Any(path => incomingCountByOwnedPath[path] > 1)) {
                items.Add(new MergedImportItem(
                    unit.SourceRelativePath,
                    desiredTarget,
                    MergeFileAction.HoldStructuralConflict));
                continue;
            }

            var owned = occupiedPaths.SingleOrDefault();
            if (owned is null) {
                items.Add(new MergedImportItem(
                    unit.SourceRelativePath, desiredTarget, MergeFileAction.PlaceNew));
                continue;
            }

            var action = DecideAgainstOwned(
                unit.FileName,
                owned,
                incomingQualityPosition,
                incomingRevision,
                properPolicy,
                allowFormatChange);
            var hasMissingClaims = claimedSlots.Any(slot => !ownedPathBySlot.ContainsKey(slot));
            var cannotDropMissingClaims = hasMissingClaims
                && action is MergeFileAction.DropNotUpgrade or MergeFileAction.DropFormatChange;
            var narrowsSharedOwner = action == MergeFileAction.ReplaceUpgrade
                && ownedSlotsByPath[owned].Any(slot => !claimedSlots.Contains(slot));
            if (cannotDropMissingClaims || narrowsSharedOwner) {
                action = MergeFileAction.HoldStructuralConflict;
            }

            items.Add(new MergedImportItem(
                unit.SourceRelativePath, owned,
                action,
                owned));
        }

        return items;
    }

    /// <summary>
    /// The shared per-file gate, also used by the movie merged flow (a movie is one unit).
    /// <paramref name="allowFormatChange"/> — the user's explicit "import anyway" — lets a genuine
    /// upgrade replace the owned file even when the extension differs; automatic imports leave it off
    /// and hold the format change for manual review.
    /// </summary>
    public static MergeFileAction DecideAgainstOwned(
        string incomingFileName,
        string ownedFilePath,
        int incomingQualityPosition,
        int incomingRevision,
        ProperDownloadPolicy properPolicy,
        bool allowFormatChange = false) {
        var ownedName = Path.GetFileNameWithoutExtension(ownedFilePath);
        var ownedPosition = (int)VideoQualityDetection.Detect(ownedName);

        // Conservative on unknowns: an unrankable incoming release never wins, and an unrankable owned
        // file is never overwritten.
        if (incomingQualityPosition <= 0 || ownedPosition <= 0) {
            return MergeFileAction.DropNotUpgrade;
        }

        var upgrade = incomingQualityPosition > ownedPosition
            || (incomingQualityPosition == ownedPosition
                && properPolicy == ProperDownloadPolicy.PreferAndUpgrade
                && incomingRevision > ReleaseRevisionDetection.Detect(ownedName));
        if (!upgrade) {
            return MergeFileAction.DropNotUpgrade;
        }

        return allowFormatChange || string.Equals(
            Path.GetExtension(incomingFileName), Path.GetExtension(ownedFilePath), StringComparison.OrdinalIgnoreCase)
            ? MergeFileAction.ReplaceUpgrade
            : MergeFileAction.DropFormatChange;
    }
}

/// <summary>
/// Pure merge planning for a music import whose album (or artist) already lives on disk: plan items are
/// re-anchored from the template-rendered album folder onto the existing one, and a track whose relative
/// path the album already owns is dropped — track filenames carry no reliable quality signal, so an
/// existing track is never replaced.
/// </summary>
public static class MusicExistingTargetMerge {
    public static IReadOnlyList<MergedImportItem> Plan(
        IReadOnlyList<ImportPlanItem> items,
        string albumFolderAbsolute,
        IReadOnlySet<string> existingRelativeFiles) {
        var merged = new List<MergedImportItem>(items.Count);
        foreach (var item in items) {
            // Template plans render as "<album folder>/<inner path>"; the inner path (disc folders,
            // track names) is preserved, only the album folder is re-anchored.
            var inner = InnerRelativePath(item.TargetRelativePath);
            merged.Add(new MergedImportItem(
                item.SourceRelativePath,
                Path.Combine(albumFolderAbsolute, inner),
                existingRelativeFiles.Contains(inner) ? MergeFileAction.DropNotUpgrade : MergeFileAction.PlaceNew));
        }

        return merged;
    }

    /// <summary>The path inside the album folder: the render's segments beyond the album-folder prefix.</summary>
    private static string InnerRelativePath(string targetRelativePath) {
        var segments = targetRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Music templates render "{Artist}/{Album}" as the folder (2 segments) plus the preserved inner
        // structure; a custom single-segment folder template degrades gracefully to dropping one segment.
        var prefix = Math.Min(2, Math.Max(segments.Length - 1, 0));
        return string.Join('/', segments[prefix..]);
    }
}
