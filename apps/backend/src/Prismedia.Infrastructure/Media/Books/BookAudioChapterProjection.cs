using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Books;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media.Books;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Media.Books;

/// <summary>
/// Projects Books' physical audiobook files into their domain audio renditions: playable tracks with
/// the exact facts each file states (duration, source path, title and track-number tags, embedded
/// chapter markers), arranged by the one audiobook track-order rule.
/// </summary>
internal static class BookAudioChapterProjection {
    #region Actions - Loading

    /// <summary>Loads one Book's audio rendition; a Book without playable tracks gets an empty one.</summary>
    public static async Task<AudiobookRendition> LoadAsync(
        PrismediaDbContext db,
        Guid bookId,
        CancellationToken cancellationToken) =>
        (await LoadManyAsync(db, [bookId], cancellationToken)).GetValueOrDefault(bookId) ?? AudiobookRendition.None;

    /// <summary>
    /// Loads the audio renditions of several Books with a fixed number of queries. Only playable tracks
    /// (not wanted, with a source file) take part, and only source-owned markers imported from the
    /// container become chapters; user timeline markers never split a track.
    /// </summary>
    /// <returns>Renditions keyed by Book id, for Books that have playable tracks.</returns>
    public static async Task<IReadOnlyDictionary<Guid, AudiobookRendition>> LoadManyAsync(
        PrismediaDbContext db,
        IReadOnlyCollection<Guid> bookIds,
        CancellationToken cancellationToken) {
        if (bookIds.Count == 0) {
            return new Dictionary<Guid, AudiobookRendition>();
        }

        var trackKind = EntityKind.AudioTrack.ToCode();
        var parentIds = bookIds.Select(id => (Guid?)id).ToArray();
        var sources = await db.Entities.AsNoTracking()
            .Where(row => parentIds.Contains(row.ParentEntityId) && row.KindCode == trackKind && !row.IsWanted)
            .Join(
                db.EntityFiles.AsNoTracking().Where(file => file.Role == EntityFileRole.Source),
                row => row.Id,
                file => file.EntityId,
                (row, file) => new { BookId = row.ParentEntityId!.Value, row.Id, row.Title, file.Path })
            .ToArrayAsync(cancellationToken);
        if (sources.Length == 0) {
            return new Dictionary<Guid, AudiobookRendition>();
        }

        var tracks = sources
            .GroupBy(source => source.Id)
            .Select(group => new {
                group.First().BookId,
                Id = group.Key,
                group.First().Title,
                Path = group.Select(source => source.Path).Min(StringComparer.Ordinal)!
            })
            .ToArray();
        var trackIds = tracks.Select(track => track.Id).ToArray();
        var markers = (await db.EntityMarkers.AsNoTracking()
                .Where(marker => trackIds.Contains(marker.EntityId) && marker.SourceIndex != null)
                .Select(marker => new {
                    marker.EntityId,
                    marker.Id,
                    marker.Title,
                    marker.Seconds,
                    marker.EndSeconds,
                    marker.Untitled
                })
                .ToArrayAsync(cancellationToken))
            .ToLookup(
                marker => marker.EntityId,
                marker => new SourceChapterMarker(marker.Id, marker.Title, marker.Seconds, marker.EndSeconds, marker.Untitled));
        var durations = await db.EntityTechnical.AsNoTracking()
            .Where(row => trackIds.Contains(row.EntityId))
            .ToDictionaryAsync(row => row.EntityId, row => row.DurationSeconds, cancellationToken);
        var tags = await db.AudioTrackDetails.AsNoTracking()
            .Where(row => trackIds.Contains(row.EntityId))
            .Select(row => new { row.EntityId, row.EmbeddedTitle, row.EmbeddedTrackNumber })
            .ToDictionaryAsync(row => row.EntityId, cancellationToken);

        return tracks
            .GroupBy(track => track.BookId)
            .ToDictionary(
                group => group.Key,
                group => new AudiobookRendition(group.Select(track => new AudioTrackSpan(
                    track.Id,
                    track.Title,
                    durations.GetValueOrDefault(track.Id),
                    markers[track.Id].ToArray(),
                    track.Path,
                    tags.GetValueOrDefault(track.Id)?.EmbeddedTitle,
                    tags.GetValueOrDefault(track.Id)?.EmbeddedTrackNumber))));
    }

    #endregion

    #region Actions - Matching

    /// <summary>
    /// The rendition's chapter windows shaped for the automatic matcher, in playback order, each with the
    /// only title that may prove its identity (an embedded chapter title or a file's title tag).
    /// </summary>
    public static IReadOnlyList<MatchableAudioChapter> Matchable(AudiobookRendition rendition) =>
        rendition.Tracks
            .SelectMany((track, trackOrder) => track.ChapterWindows().Select((window, markerOrder) => new MatchableAudioChapter(
                window.TrackEntityId,
                window.MarkerId,
                window.Title,
                track.IdentifyingTitle(window),
                trackOrder,
                markerOrder,
                window.StartSeconds,
                window.EndSeconds)))
            .ToArray();

    #endregion
}
