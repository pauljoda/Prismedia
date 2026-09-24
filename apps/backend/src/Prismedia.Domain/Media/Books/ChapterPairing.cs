using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Media.Books;

/// <summary>One persisted association between a readable chapter and an audio chapter window.</summary>
/// <param name="ChapterKey">Readable chapter key.</param>
/// <param name="TrackEntityId">Physical audio track of the paired window.</param>
/// <param name="MarkerId">Embedded marker of the paired window, or null for the whole track.</param>
/// <param name="Origin">Whether a user chose the pair or the matcher derived it.</param>
public sealed record ChapterPairing(
    string ChapterKey,
    Guid TrackEntityId,
    Guid? MarkerId,
    BookChapterMappingOrigin Origin);
