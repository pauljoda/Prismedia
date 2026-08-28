namespace Prismedia.Contracts.Books;

/// <summary>
/// Associates one readable chapter location with one audiobook track Entity.
/// </summary>
/// <param name="ReadableChapterKey">Stable location key from the readable book contents.</param>
/// <param name="AudioTrackId">Identifier of the audiobook track Entity.</param>
/// <param name="Origin">
/// Mapping provenance code (<c>manual</c> or <c>auto</c>). Ignored on save requests, where every
/// submitted pair is manual by definition.
/// </param>
/// <param name="AudioMarkerId">
/// Optional marker identifying one chapter window inside <paramref name="AudioTrackId"/>. Null
/// means the entire physical audio track is the chapter source.
/// </param>
public sealed record BookChapterAudioMapping(
    string ReadableChapterKey,
    Guid AudioTrackId,
    string? Origin = null,
    Guid? AudioMarkerId = null);

/// <summary>One addressable audiobook chapter, backed by a whole track or an embedded marker.</summary>
/// <param name="AudioTrackId">Physical playable audio-track Entity.</param>
/// <param name="AudioMarkerId">Embedded marker identity, or null for the whole track.</param>
/// <param name="Title">Chapter label shown to readers and used by the automatic matcher.</param>
/// <param name="StartSeconds">Start offset inside the physical track.</param>
/// <param name="EndSeconds">End offset inside the physical track, when known.</param>
public sealed record BookAudioChapter(
    Guid AudioTrackId,
    Guid? AudioMarkerId,
    string Title,
    double StartSeconds,
    double? EndSeconds);

/// <summary>
/// Persisted audiobook-to-readable-chapter associations for one Book.
/// </summary>
/// <param name="Mappings">The Book's explicit one-to-one chapter associations.</param>
/// <param name="AudioChapters">Addressable audio chapters available for automatic or manual mapping.</param>
public sealed record BookChapterMappingsResponse(
    IReadOnlyList<BookChapterAudioMapping> Mappings,
    IReadOnlyList<BookAudioChapter> AudioChapters);

/// <summary>
/// Replaces all explicit audiobook-to-readable-chapter associations for one Book.
/// </summary>
/// <param name="Mappings">The complete desired one-to-one chapter map.</param>
public sealed record ReplaceBookChapterMappingsRequest(IReadOnlyList<BookChapterAudioMapping> Mappings);
