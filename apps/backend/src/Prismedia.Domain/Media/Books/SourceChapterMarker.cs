namespace Prismedia.Domain.Media.Books;

/// <summary>An embedded chapter marker imported from a track's media container.</summary>
/// <param name="MarkerId">Persisted marker identity.</param>
/// <param name="Title">Chapter label from the container.</param>
/// <param name="StartSeconds">Chapter start inside the track.</param>
/// <param name="EndSeconds">Chapter end inside the track, when the container declares one.</param>
public sealed record SourceChapterMarker(Guid MarkerId, string Title, double StartSeconds, double? EndSeconds);
