namespace Prismedia.Domain.Media.Books;

/// <summary>
/// One playable audio track of a work, in playback order, with its probed duration. The span turns
/// its embedded chapter markers into addressable chapter windows.
/// </summary>
/// <param name="TrackEntityId">Physical playable audio track.</param>
/// <param name="Title">Track title, used for the whole-track window.</param>
/// <param name="DurationSeconds">Probed duration, when known.</param>
public sealed record AudioTrackSpan(Guid TrackEntityId, string Title, double? DurationSeconds) {
    #region Actions - Windows

    /// <summary>Known positive duration, or null while the track is unprobed.</summary>
    public double? KnownDuration() =>
        DurationSeconds is { } duration && double.IsFinite(duration) && duration > 0 ? duration : null;

    /// <summary>
    /// Addressable chapter windows of this track. Without markers the whole track is one window
    /// <c>[0, duration)</c>. Each marker covers <c>[start, declared end ?? next marker start ??
    /// duration)</c>, flagging an end that was derived rather than declared.
    /// </summary>
    /// <param name="markers">Source-owned chapter markers of this track, in any order.</param>
    public IReadOnlyList<AudioChapterWindow> ChapterWindows(IReadOnlyList<SourceChapterMarker> markers) {
        if (markers.Count == 0) {
            return [new AudioChapterWindow(TrackEntityId, null, Title, 0, KnownDuration(), EndInferred: false)];
        }

        var ordered = markers
            .OrderBy(marker => marker.StartSeconds)
            .ThenBy(marker => marker.MarkerId)
            .ToArray();
        var windows = new List<AudioChapterWindow>(ordered.Length);
        for (var index = 0; index < ordered.Length; index++) {
            var marker = ordered[index];
            var inferredEnd = index + 1 < ordered.Length ? ordered[index + 1].StartSeconds : KnownDuration();
            windows.Add(new AudioChapterWindow(
                TrackEntityId,
                marker.MarkerId,
                marker.Title,
                marker.StartSeconds,
                marker.EndSeconds ?? inferredEnd,
                EndInferred: marker.EndSeconds is null && inferredEnd is not null));
        }
        return windows;
    }

    #endregion
}
