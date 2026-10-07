namespace Prismedia.Domain.Media.Books;

public sealed partial class WorkAlignment {
    #region Actions - Titles

    /// <summary>
    /// The title the shared player presents while an audio window plays: its paired readable chapter's
    /// title, else the title the file itself declares (an embedded chapter title or the file's title
    /// tag). Null when neither exists, so the player presents the work's own title instead; a
    /// placeholder for an untitled chapter and a file name never stand in for a title.
    /// </summary>
    /// <param name="trackEntityId">Physical audio track of the window.</param>
    /// <param name="markerId">Embedded chapter marker of the window, or null for the whole track.</param>
    public string? ListeningTitle(Guid trackEntityId, Guid? markerId) {
        var pairedTitle = Rows
            .FirstOrDefault(row => row.IsPaired && row.Audio!.Identifies(trackEntityId, markerId))?
            .Readable?.Title;
        if (!string.IsNullOrWhiteSpace(pairedTitle)) {
            return pairedTitle;
        }

        var track = Audio.Tracks.FirstOrDefault(candidate => candidate.TrackEntityId == trackEntityId);
        var window = Audio.Windows.FirstOrDefault(candidate => candidate.Identifies(trackEntityId, markerId));
        return track is null || window is null ? null : track.IdentifyingTitle(window);
    }

    #endregion
}
