namespace Prismedia.Domain.Media.Books;

/// <summary>An embedded chapter marker imported from a track's media container.</summary>
/// <param name="MarkerId">Persisted marker identity.</param>
/// <param name="Title">Chapter label shown to listeners; a placeholder such as "Chapter 3" when <paramref name="Untitled"/>.</param>
/// <param name="StartSeconds">Chapter start inside the track.</param>
/// <param name="EndSeconds">Chapter end inside the track, when the container declares one.</param>
/// <param name="Untitled">
/// Whether the container declared no title, so <paramref name="Title"/> is a display placeholder that
/// never proves chapter identity.
/// </param>
public sealed record SourceChapterMarker(
    Guid MarkerId,
    string Title,
    double StartSeconds,
    double? EndSeconds,
    bool Untitled = false) {
    #region Actions - Presentation

    /// <summary>
    /// Display placeholder for a chapter the container left untitled: its one-based position among the
    /// source's chapters. This is the only place the placeholder is spelled, so the probe and the
    /// matcher agree it is not a real title.
    /// </summary>
    /// <param name="sourceIndex">Zero-based chapter index reported by the container.</param>
    public static string PlaceholderTitle(int sourceIndex) => $"Chapter {sourceIndex + 1}";

    #endregion
}
