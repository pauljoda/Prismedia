using Prismedia.Domain.Capabilities;

namespace Prismedia.Domain.Media.Books;

/// <summary>
/// A work's audio rendition: its playable tracks in the one playback order every consumer shares
/// (scan, probe, projection, matcher, and cumulative listening time), the chapter windows they expose,
/// and the structure those tracks form.
/// </summary>
public sealed class AudiobookRendition {
    #region Static Variables

    /// <summary>A work without playable audio.</summary>
    public static readonly AudiobookRendition None = new([]);

    #endregion

    #region Variables

    /// <summary>Playable tracks in playback order.</summary>
    public IReadOnlyList<AudioTrackSpan> Tracks { get; }

    /// <summary>Addressable chapter windows in playback order: track order, then start time.</summary>
    public IReadOnlyList<AudioChapterWindow> Windows { get; }

    /// <summary>How the tracks divide into chapters, or null without playable audio.</summary>
    public AudiobookStructureDefinition? Structure { get; }

    /// <summary>Seconds of probed audio across every track; unprobed tracks count as zero.</summary>
    public double TotalKnownSeconds { get; }

    /// <summary>Whether the work has playable audio.</summary>
    public bool HasAudio => Tracks.Count > 0;

    #endregion

    #region Constructors

    /// <summary>Builds the rendition, arranging <paramref name="tracks"/> in playback order.</summary>
    /// <param name="tracks">The work's playable tracks, in any order.</param>
    public AudiobookRendition(IEnumerable<AudioTrackSpan> tracks) {
        Tracks = InPlaybackOrder(
            tracks.OrderBy(track => track.TrackEntityId),
            track => track.SourcePath ?? track.Title,
            track => track.TrackNumberTag);
        Windows = Tracks.SelectMany(track => track.ChapterWindows()).ToArray();
        Structure = AudiobookStructureDefinition.Of(Tracks);
        TotalKnownSeconds = Tracks.Sum(track => track.KnownDuration() ?? 0);
    }

    #endregion

    #region Actions - Order

    /// <summary>
    /// The single audiobook track-order rule: embedded track numbers when every file has a distinct
    /// positive one, otherwise natural file-path order ("Part 2" before "Part 10"). Ties keep the input
    /// order, so callers pass tracks in a stable order.
    /// </summary>
    /// <typeparam name="TTrack">Any track shape that knows its source path and track-number tag.</typeparam>
    /// <param name="tracks">Tracks of one work, in any stable order.</param>
    /// <param name="sourcePath">The track's source file path.</param>
    /// <param name="trackNumberTag">The track's embedded track-number tag, when known.</param>
    /// <returns>The tracks in playback order.</returns>
    public static IReadOnlyList<TTrack> InPlaybackOrder<TTrack>(
        IEnumerable<TTrack> tracks,
        Func<TTrack, string> sourcePath,
        Func<TTrack, int?> trackNumberTag) {
        var items = tracks.ToArray();
        var numbers = items.Select(trackNumberTag).ToArray();
        var numberedDistinctly = items.Length > 0 &&
            numbers.All(number => number is > 0) &&
            numbers.Distinct().Count() == numbers.Length;
        return numberedDistinctly
            ? items.OrderBy(track => trackNumberTag(track)!.Value).ToArray()
            : items.OrderBy(sourcePath, NaturalPathComparer.Instance).ToArray();
    }

    #endregion

    #region Actions - Identity

    /// <summary>
    /// The title that can prove which chapter <paramref name="window"/> is, or null when the window has
    /// only a placeholder or file-name title. See <see cref="AudioTrackSpan.IdentifyingTitle"/>.
    /// </summary>
    public string? IdentifyingTitle(AudioChapterWindow window) =>
        Tracks.FirstOrDefault(track => track.TrackEntityId == window.TrackEntityId)?.IdentifyingTitle(window);

    #endregion

    #region Actions - Listening Time

    /// <summary>
    /// Seconds listened before <paramref name="listening"/> across the ordered tracks: every earlier
    /// track's probed duration plus the offset inside the checkpoint's track, capped at that track's
    /// duration. Null when the checkpoint's track is not part of this rendition.
    /// </summary>
    /// <exception cref="ArgumentException">The checkpoint is not offset-addressed.</exception>
    public double? ElapsedSeconds(ProgressCheckpoint listening) {
        if (!listening.Definition.AddressesByOffset) {
            throw new ArgumentException("Elapsed listening time needs a listening checkpoint.", nameof(listening));
        }

        var elapsed = 0d;
        foreach (var track in Tracks) {
            if (track.TrackEntityId == listening.PositionEntityId) {
                var offset = listening.OffsetSeconds ?? listening.Index;
                return elapsed + (track.KnownDuration() is { } duration ? Math.Min(offset, duration) : offset);
            }
            elapsed += track.KnownDuration() ?? 0;
        }
        return null;
    }

    /// <summary>
    /// Share (0..1) of the known audio listened before <paramref name="listening"/>, or null when no
    /// track has a probed duration or the checkpoint's track is not part of this rendition.
    /// </summary>
    /// <exception cref="ArgumentException">The checkpoint is not offset-addressed.</exception>
    public double? ListenedFraction(ProgressCheckpoint listening) =>
        TotalKnownSeconds > 0 && ElapsedSeconds(listening) is { } elapsed
            ? Math.Clamp(elapsed / TotalKnownSeconds, 0, 1)
            : null;

    #endregion
}
