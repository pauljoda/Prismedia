using Prismedia.Domain.Capabilities;

namespace Prismedia.Domain.Media.Books;

public sealed partial class WorkAlignment {
    #region Actions - Format Progress

    /// <summary>
    /// Share (0..1) of the readable rendition before the reading checkpoint, from the reader's own
    /// position only: a normalized whole-work locator, a page of the whole work, or a page of a chapter
    /// placed after every earlier paged chapter. Null when the position cannot be placed in the work.
    /// Listening never contributes.
    /// </summary>
    /// <exception cref="ArgumentException">The checkpoint is offset-addressed (listening).</exception>
    public double? ReadingFraction(ProgressCheckpoint reading) {
        if (reading.Definition.AddressesByOffset) {
            throw new ArgumentException("Reading progress needs a reading checkpoint.", nameof(reading));
        }

        if (reading.Unit == reading.Definition.PositionUnit) {
            return reading.Total > 0 ? Math.Clamp(reading.Index / (double)reading.Total, 0, 1) : null;
        }
        if (reading.PositionEntityId == WorkId) {
            return reading.Total > 0 ? Math.Clamp((reading.Index + 1) / (double)reading.Total, 0, 1) : null;
        }

        var paged = _readableChapters.Where(chapter => chapter.HasPageBounds()).ToArray();
        var totalPages = paged.Sum(chapter => chapter.PageCount!.Value);
        var pagesBefore = 0;
        foreach (var chapter in paged) {
            if (chapter.ChapterEntityId == reading.PositionEntityId) {
                var page = Math.Clamp(reading.Index, 0, chapter.PageCount!.Value - 1);
                return Math.Clamp((pagesBefore + page + 1) / (double)totalPages, 0, 1);
            }
            pagesBefore += chapter.PageCount!.Value;
        }
        return null;
    }

    /// <summary>
    /// Share (0..1) of the known audio listened before the listening checkpoint, across the ordered
    /// tracks. Null when no track has a probed duration or the track is no longer part of the work.
    /// </summary>
    /// <exception cref="ArgumentException">The checkpoint is not offset-addressed.</exception>
    public double? ListeningFraction(ProgressCheckpoint listening) => Audio.ListenedFraction(listening);

    #endregion
}
