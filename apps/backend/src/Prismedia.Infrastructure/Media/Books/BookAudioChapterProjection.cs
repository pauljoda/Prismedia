using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Books;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media.Books;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Media.Books;

/// <summary>A Book's playable tracks in playback order and the audio chapter windows they expose.</summary>
/// <param name="Tracks">Playable tracks with their probed durations.</param>
/// <param name="Windows">Whole-track or source-marker chapter windows in playback order.</param>
/// <param name="Matchable">The same windows shaped for the automatic chapter matcher.</param>
internal sealed record BookAudioChapters(
    IReadOnlyList<AudioTrackSpan> Tracks,
    IReadOnlyList<AudioChapterWindow> Windows,
    IReadOnlyList<MatchableAudioChapter> Matchable);

/// <summary>Projects physical audiobook files into addressable whole-track or marker chapters.</summary>
internal static class BookAudioChapterProjection {
    #region Actions - Loading

    /// <summary>
    /// Loads a Book's playable tracks ordered by (sort order, title, id) and their chapter windows.
    /// Only source-owned markers imported from the container become chapters; user timeline markers
    /// never split a track.
    /// </summary>
    public static async Task<BookAudioChapters> LoadAsync(
        PrismediaDbContext db,
        Guid bookId,
        CancellationToken cancellationToken) {
        var trackKind = EntityKind.AudioTrack.ToCode();
        var tracks = (await db.Entities.AsNoTracking()
                .Where(row => row.ParentEntityId == bookId && row.KindCode == trackKind && !row.IsWanted &&
                    db.EntityFiles.Any(file => file.EntityId == row.Id && file.Role == EntityFileRole.Source))
                .Select(row => new { row.Id, row.Title, row.SortOrder })
                .ToArrayAsync(cancellationToken))
            .OrderBy(track => track.SortOrder ?? int.MaxValue)
            .ThenBy(track => track.Title, StringComparer.Ordinal)
            .ThenBy(track => track.Id)
            .ToArray();
        var trackIds = tracks.Select(track => track.Id).ToArray();
        var markers = (await db.EntityMarkers.AsNoTracking()
                .Where(marker => trackIds.Contains(marker.EntityId) && marker.SourceIndex != null)
                .Select(marker => new { marker.EntityId, marker.Id, marker.Title, marker.Seconds, marker.EndSeconds })
                .ToArrayAsync(cancellationToken))
            .ToLookup(
                marker => marker.EntityId,
                marker => new SourceChapterMarker(marker.Id, marker.Title, marker.Seconds, marker.EndSeconds));
        var durations = await db.EntityTechnical.AsNoTracking()
            .Where(row => trackIds.Contains(row.EntityId))
            .ToDictionaryAsync(row => row.EntityId, row => row.DurationSeconds, cancellationToken);

        var spans = new List<AudioTrackSpan>(tracks.Length);
        var windows = new List<AudioChapterWindow>();
        var matchable = new List<MatchableAudioChapter>();
        foreach (var track in tracks) {
            var span = new AudioTrackSpan(track.Id, track.Title, durations.GetValueOrDefault(track.Id));
            spans.Add(span);
            var trackWindows = span.ChapterWindows(markers[track.Id].ToArray());
            windows.AddRange(trackWindows);
            matchable.AddRange(trackWindows.Select((window, markerOrder) => new MatchableAudioChapter(
                window.TrackEntityId,
                window.MarkerId,
                window.Title,
                track.SortOrder ?? 0,
                markerOrder,
                window.StartSeconds,
                window.EndSeconds)));
        }
        return new BookAudioChapters(spans, windows, matchable);
    }

    #endregion
}
