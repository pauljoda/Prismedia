namespace Prismedia.Domain.Media.Books;

/// <summary>
/// One addressable audio chapter: a whole physical track, or one source-owned embedded chapter
/// marker inside it, with the time window it covers. A window without a known end is unwindowed and
/// can only be aligned at its start.
/// </summary>
/// <param name="TrackEntityId">Physical playable audio track.</param>
/// <param name="MarkerId">Embedded chapter marker, or null for the whole track.</param>
/// <param name="Title">Chapter label shown to listeners.</param>
/// <param name="StartSeconds">Window start inside the physical track.</param>
/// <param name="EndSeconds">Window end inside the physical track, when known.</param>
/// <param name="EndInferred">Whether the end came from the next marker or the track duration.</param>
public sealed record AudioChapterWindow(
    Guid TrackEntityId,
    Guid? MarkerId,
    string Title,
    double StartSeconds,
    double? EndSeconds,
    bool EndInferred) {
    #region Static Variables

    /// <summary>
    /// Seconds of runway placed before an interpolated listening position so the listener re-hears
    /// the words leading into it. The runway never crosses the window start.
    /// </summary>
    public const double ListeningRunwaySeconds = 5;

    #endregion

    #region Actions - Bounds

    /// <summary>Whether the window has a known end after its start.</summary>
    public bool IsWindowed() => EndSeconds is { } end && end > StartSeconds;

    /// <summary>Whether this window identifies <paramref name="trackEntityId"/> and <paramref name="markerId"/>.</summary>
    public bool Identifies(Guid trackEntityId, Guid? markerId) =>
        TrackEntityId == trackEntityId && MarkerId == markerId;

    /// <summary>Whether <paramref name="offsetSeconds"/> on the window's track lies in <c>[start, end)</c>.</summary>
    public bool Contains(double offsetSeconds) =>
        offsetSeconds >= StartSeconds && (EndSeconds is not { } end || offsetSeconds < end);

    #endregion

    #region Actions - Interpolation

    /// <summary>Relative position (0..1) of a track offset inside this window; 0 when unwindowed.</summary>
    public double PositionOf(double offsetSeconds) =>
        IsWindowed()
            ? Clamp((offsetSeconds - StartSeconds) / (EndSeconds!.Value - StartSeconds))
            : 0;

    /// <summary>
    /// Listening target at relative position <paramref name="position"/>:
    /// <c>max(start, start + φ(end − start) − runway)</c>, or the window start when unwindowed.
    /// </summary>
    public ListeningTarget TargetAt(double position) {
        var offset = IsWindowed()
            ? Math.Max(
                StartSeconds,
                StartSeconds + Clamp(position) * (EndSeconds!.Value - StartSeconds) - ListeningRunwaySeconds)
            : StartSeconds;
        return new ListeningTarget(TrackEntityId, MarkerId, offset);
    }

    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    #endregion
}
