using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities.Thumbnails;

/// <summary>
/// Contributes inbound reference counts to taxonomy (person/studio/tag) thumbnails. For each such
/// entity on the page it counts the distinct source entities that link to it, grouped by the source
/// kind, and surfaces both the structured <see cref="EntityKindCount"/> list and per-icon count
/// chips (for example "10" beside a video icon). The rule is kind-agnostic: any relationship code or
/// new taxonomy kind flows through unchanged. Counts are computed live from the relationship links —
/// there is nothing cached to invalidate, and hard deletes cascade the links away, so a count only
/// ever reflects entities that currently exist. Collections are intentionally excluded: their
/// membership lives in the collection item table (and smart collections are rule-based), not in the
/// relationship links, so they need a separate contributor.
/// </summary>
internal sealed class ReferenceCountContributor(PrismediaDbContext db) : IThumbnailContributor {
    /// <inheritdoc />
    public async Task ContributeAsync(
        ThumbnailContributions contributions,
        CancellationToken cancellationToken) {
        var targetIds = contributions.Rows
            .Where(row => AccumulatesReferences(row.KindCode))
            .Select(row => row.Id)
            .ToArray();
        if (targetIds.Length == 0) {
            return;
        }

        // Distinct source entities per (target, source kind). The join to entities resolves the
        // source kind; a source linked under several relationship codes (e.g. cast and director)
        // counts once per kind via COUNT(DISTINCT source id).
        var grouped = await db.EntityRelationshipLinks.AsNoTracking()
            .Where(link => targetIds.Contains(link.TargetEntityId))
            .Join(
                db.Entities.AsNoTracking(),
                link => link.EntityId,
                source => source.Id,
                (link, source) => new { link.TargetEntityId, source.KindCode, source.Id })
            .GroupBy(row => new { row.TargetEntityId, row.KindCode })
            .Select(group => new {
                group.Key.TargetEntityId,
                group.Key.KindCode,
                Count = group.Select(row => row.Id).Distinct().Count()
            })
            .ToArrayAsync(cancellationToken);

        foreach (var perTarget in grouped.GroupBy(row => row.TargetEntityId)) {
            var counts = perTarget
                .OrderByDescending(row => row.Count)
                .ThenBy(row => row.KindCode, StringComparer.Ordinal)
                .Select(row => new EntityKindCount(row.KindCode, row.Count))
                .ToArray();
            contributions.SetReferenceCounts(perTarget.Key, counts);

            // Chips merge kinds that share an icon (movies, series, and videos all read as "video")
            // so a card shows one count per glyph rather than several identical icons; the structured
            // counts above stay granular for compatibility layers.
            var chips = counts
                .GroupBy(count => ChipIcon(count.Kind))
                .Select(group => new { Icon = group.Key, Count = group.Sum(count => count.Count) })
                .OrderByDescending(chip => chip.Count)
                .ThenBy(chip => chip.Icon, StringComparer.Ordinal);
            foreach (var chip in chips) {
                contributions.AddMeta(perTarget.Key, chip.Icon, chip.Count.ToString());
            }
        }
    }

    /// <summary>
    /// Whether a kind carries an inbound-reference concept counted from relationship links. Taxonomy
    /// entities (person/studio/tag) are referenced by media; media entities are not, so they are
    /// skipped and media grids issue no reference-count query. Collections are excluded because their
    /// membership is not modeled as relationship links.
    /// </summary>
    private static bool AccumulatesReferences(string kindCode) =>
        EntityKindRegistry.TryGet(kindCode, out var kind) &&
        EntityKindRegistry.Describe(kind).Category is EntityKindCategory.Taxonomy;

    /// <summary>
    /// Maps a source kind code to a thumbnail meta icon so the count chip reads as, for example,
    /// "10 videos". Falls back to the generic count icon for kinds without a dedicated glyph.
    /// </summary>
    private static string ChipIcon(string kindCode) => kindCode switch {
        var code when code == EntityKindRegistry.Video.Code => "video",
        var code when code == EntityKindRegistry.Movie.Code => "video",
        var code when code == EntityKindRegistry.VideoSeries.Code => "video",
        var code when code == EntityKindRegistry.VideoSeason.Code => "video",
        var code when code == EntityKindRegistry.Image.Code => "image",
        var code when code == EntityKindRegistry.Gallery.Code => "gallery",
        var code when code == EntityKindRegistry.Audio.Code => "audio",
        var code when code == EntityKindRegistry.AudioTrack.Code => "audio",
        var code when code == EntityKindRegistry.AudioLibrary.Code => "audio",
        var code when code == EntityKindRegistry.MusicArtist.Code => "audio",
        var code when code == EntityKindRegistry.Book.Code => "book",
        var code when code == EntityKindRegistry.Collection.Code => "collection",
        var code when code == EntityKindRegistry.Person.Code => "person",
        var code when code == EntityKindRegistry.Studio.Code => "studio",
        var code when code == EntityKindRegistry.Tag.Code => "tag",
        _ => "count"
    };
}
