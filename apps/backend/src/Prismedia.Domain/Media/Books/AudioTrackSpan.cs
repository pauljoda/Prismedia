namespace Prismedia.Domain.Media.Books;

/// <summary>
/// One playable audio track of a work with the facts its file states exactly: probed duration,
/// embedded chapter markers, source path, and embedded title and track-number tags. The span turns its
/// markers into addressable chapter windows and says which window titles may prove chapter identity.
/// </summary>
/// <param name="TrackEntityId">Physical playable audio track.</param>
/// <param name="Title">Track display title, used for the whole-track window (often the file name).</param>
/// <param name="DurationSeconds">Probed duration, when known.</param>
/// <param name="Markers">Source-owned embedded chapter markers of this track, in any order.</param>
/// <param name="SourcePath">Source file path, used for playback order and part-name recognition.</param>
/// <param name="TitleTag">Embedded title tag of the file, when the container declares one.</param>
/// <param name="TrackNumberTag">Embedded track-number tag of the file, when the container declares one.</param>
public sealed record AudioTrackSpan(
    Guid TrackEntityId,
    string Title,
    double? DurationSeconds,
    IReadOnlyList<SourceChapterMarker>? Markers = null,
    string? SourcePath = null,
    string? TitleTag = null,
    int? TrackNumberTag = null) {
    #region Variables

    /// <summary>Whether the file carries embedded chapters read from its container.</summary>
    public bool HasEmbeddedChapters => (Markers ?? []).Count > 0;

    /// <summary>File name without extension, falling back to the display title for pathless tracks.</summary>
    public string FileStem => Path.GetFileNameWithoutExtension(SourcePath ?? Title);

    #endregion

    #region Actions - Windows

    /// <summary>Known positive duration, or null while the track is unprobed.</summary>
    public double? KnownDuration() =>
        DurationSeconds is { } duration && double.IsFinite(duration) && duration > 0 ? duration : null;

    /// <summary>
    /// Addressable chapter windows of this track. Without markers the whole track is one window
    /// <c>[0, duration)</c>. Each marker covers <c>[start, declared end ?? next marker start ??
    /// duration)</c>, flagging an end that was derived rather than declared.
    /// </summary>
    public IReadOnlyList<AudioChapterWindow> ChapterWindows() {
        var markers = Markers ?? [];
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

    #region Actions - Identity

    /// <summary>
    /// The title that can prove which chapter <paramref name="window"/> is: an embedded chapter's own
    /// title, or a whole file's embedded title tag. A placeholder for an untitled chapter and a whole
    /// file's display title (usually its file name) never prove identity, so they return null.
    /// </summary>
    /// <param name="window">A window of this track.</param>
    public string? IdentifyingTitle(AudioChapterWindow window) {
        if (window.MarkerId is not { } markerId) {
            return string.IsNullOrWhiteSpace(TitleTag) ? null : TitleTag;
        }

        var marker = (Markers ?? []).FirstOrDefault(candidate => candidate.MarkerId == markerId);
        return marker is null || marker.Untitled || string.IsNullOrWhiteSpace(marker.Title) ? null : marker.Title;
    }

    #endregion
}
