using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Entities.Thumbnails;

/// <summary>
/// Adds the persisted disc or section label to audio-track thumbnails. This contributor owns the
/// audio-detail table's presentation concern and self-selects its declared kind, leaving the shared
/// thumbnail projection free of audio-specific joins.
/// </summary>
internal sealed class AudioSectionLabelThumbnailContributor(PrismediaDbContext db) : IThumbnailContributor {
    private static readonly string AudioTrackCode = EntityKind.AudioTrack.ToCode();

    /// <inheritdoc />
    public async Task ContributeAsync(
        ThumbnailContributions contributions,
        CancellationToken cancellationToken) {
        var trackIds = contributions.Rows
            .Where(row => row.KindCode == AudioTrackCode)
            .Select(row => row.Id)
            .ToArray();
        if (trackIds.Length == 0) {
            return;
        }

        var labels = await db.AudioTrackDetails.AsNoTracking()
            .Where(detail => trackIds.Contains(detail.EntityId) && detail.SectionLabel != null)
            .Select(detail => new { detail.EntityId, Label = detail.SectionLabel! })
            .ToArrayAsync(cancellationToken);
        foreach (var label in labels) {
            contributions.AddMeta(label.EntityId, EntityThumbnailMetaIcons.Disc, label.Label);
        }
    }
}
