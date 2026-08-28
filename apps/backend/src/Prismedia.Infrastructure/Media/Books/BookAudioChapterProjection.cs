using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Books;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Media.Books;

/// <summary>Projects physical audiobook files into addressable whole-track or marker chapters.</summary>
internal static class BookAudioChapterProjection {
    public static async Task<IReadOnlyList<MatchableAudioChapter>> LoadAsync(
        PrismediaDbContext db,
        Guid bookId,
        CancellationToken cancellationToken) {
        var trackKind = EntityKind.AudioTrack.ToCode();
        var tracks = await db.Entities.AsNoTracking()
            .Where(row => row.ParentEntityId == bookId && row.KindCode == trackKind && !row.IsWanted &&
                db.EntityFiles.Any(file => file.EntityId == row.Id && file.Role == EntityFileRole.Source))
            .Select(row => new { row.Id, row.Title, row.SortOrder })
            .ToArrayAsync(cancellationToken);
        var trackIds = tracks.Select(track => track.Id).ToArray();
        var markers = await db.EntityMarkers.AsNoTracking()
            .Where(marker => trackIds.Contains(marker.EntityId))
            .OrderBy(marker => marker.Seconds)
            .ThenBy(marker => marker.Id)
            .ToArrayAsync(cancellationToken);
        var durations = await db.EntityTechnical.AsNoTracking()
            .Where(row => trackIds.Contains(row.EntityId))
            .ToDictionaryAsync(row => row.EntityId, row => row.DurationSeconds, cancellationToken);

        return tracks
            .OrderBy(track => track.SortOrder ?? int.MaxValue)
            .ThenBy(track => track.Title, StringComparer.Ordinal)
            .ThenBy(track => track.Id)
            .SelectMany(track => {
                var trackMarkers = markers.Where(marker => marker.EntityId == track.Id).ToArray();
                if (trackMarkers.Length == 0) {
                    durations.TryGetValue(track.Id, out var duration);
                    return [new MatchableAudioChapter(
                        track.Id,
                        null,
                        track.Title,
                        track.SortOrder ?? 0,
                        0,
                        0,
                        duration)];
                }

                return trackMarkers.Select((marker, markerOrder) => new MatchableAudioChapter(
                    track.Id,
                    marker.Id,
                    marker.Title,
                    track.SortOrder ?? 0,
                    markerOrder,
                    marker.Seconds,
                    marker.EndSeconds)).ToArray();
            })
            .ToArray();
    }
}
