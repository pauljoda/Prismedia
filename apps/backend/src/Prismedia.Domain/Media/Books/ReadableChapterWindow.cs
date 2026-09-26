using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media.Books;

/// <summary>
/// One readable chapter and the part of the readable rendition it covers: a whole-work fraction
/// range for an EPUB table-of-contents entry, or a chapter Entity with its page count for paged
/// books. A chapter without bounds is unwindowed and can only be aligned at its start.
/// </summary>
/// <param name="ChapterKey">Stable chapter key (EPUB navigation target or chapter Entity id).</param>
/// <param name="Title">Chapter title shown to readers.</param>
/// <param name="Depth">Zero-based table-of-contents nesting depth.</param>
/// <param name="Location">EPUB navigation target that opens the chapter, when the rendition has one.</param>
/// <param name="ChapterEntityId">Chapter Entity for paged books.</param>
/// <param name="StartFraction">Whole-work start fraction for EPUB chapters.</param>
/// <param name="EndFraction">Whole-work end fraction for EPUB chapters.</param>
/// <param name="PageCount">Page count of a paged chapter.</param>
public sealed record ReadableChapterWindow(
    string ChapterKey,
    string Title,
    int Depth,
    string? Location,
    Guid? ChapterEntityId,
    double? StartFraction,
    double? EndFraction,
    int? PageCount) {
    #region Actions - Bounds

    /// <summary>Whether the chapter covers a known non-empty whole-work fraction range.</summary>
    public bool HasFractionBounds() =>
        StartFraction is { } start && EndFraction is { } end && end > start;

    /// <summary>Whether the chapter is a chapter Entity with a known page count.</summary>
    public bool HasPageBounds() => ChapterEntityId is not null && PageCount is > 0;

    /// <summary>Whether positions inside the chapter can be expressed relative to its bounds.</summary>
    public bool IsWindowed() => HasFractionBounds() || HasPageBounds();

    /// <summary>Whether the whole-work fraction <paramref name="fraction"/> falls inside the chapter.</summary>
    public bool ContainsFraction(double fraction) =>
        HasFractionBounds() && fraction >= StartFraction!.Value && fraction <= EndFraction!.Value;

    #endregion

    #region Actions - Interpolation

    /// <summary>
    /// Relative position (0..1) of a reading checkpoint inside this chapter: the page share for a
    /// paged chapter, the fraction share for an EPUB chapter, or 0 when the chapter is unwindowed.
    /// </summary>
    public double PositionOf(ProgressCheckpoint reading) {
        if (HasPageBounds()) {
            return Clamp(reading.Index / (double)PageCount!.Value);
        }
        if (HasFractionBounds() && reading.Total > 0) {
            var fraction = reading.Index / (double)reading.Total;
            return Clamp((fraction - StartFraction!.Value) / (EndFraction!.Value - StartFraction.Value));
        }
        return 0;
    }

    /// <summary>
    /// Readable target at relative position <paramref name="position"/> inside this chapter. Paged
    /// chapters open at <c>min(P − 1, ⌊φP⌋)</c>; EPUB chapters carry the whole-work index
    /// <c>round((start + φ(end − start)) · total)</c> plus the chapter location and φ so clients can
    /// open by fraction or by location and progression.
    /// </summary>
    /// <param name="workId">Work Entity addressed by whole-work reading positions.</param>
    /// <param name="position">Relative position inside the chapter, clamped to 0..1.</param>
    /// <param name="mode">Reader layout to reopen with, when known.</param>
    public ReadingTarget TargetAt(Guid workId, double position, ReaderMode? mode) {
        var phi = Clamp(position);
        if (ChapterEntityId is { } chapterEntityId) {
            var pageCount = Math.Max(0, PageCount ?? 0);
            var pageIndex = pageCount > 0 ? Math.Min(pageCount - 1, (int)Math.Floor(phi * pageCount)) : 0;
            return new ReadingTarget(
                chapterEntityId,
                ProgressUnit.Page,
                pageIndex,
                pageCount,
                Location: null,
                ChapterKey,
                Location,
                phi,
                pageIndex,
                mode);
        }

        // Whole-work reading positions use the reading modality's normalized unit and total.
        var reading = ConsumptionModalityDefinition.Reading;
        var total = reading.PositionTotal!.Value;
        var index = HasFractionBounds()
            ? (int)Math.Round((StartFraction!.Value + phi * (EndFraction!.Value - StartFraction.Value)) * total)
            : 0;
        return new ReadingTarget(
            workId,
            reading.PositionUnit!.Value,
            Math.Clamp(index, 0, total),
            total,
            Location: null,
            ChapterKey,
            Location,
            phi,
            PageIndex: null,
            mode);
    }

    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    #endregion
}
