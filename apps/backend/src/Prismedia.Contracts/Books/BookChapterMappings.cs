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
public sealed record BookChapterAudioMapping(string ReadableChapterKey, Guid AudioTrackId, string? Origin = null);

/// <summary>
/// Persisted audiobook-to-readable-chapter associations for one Book.
/// </summary>
/// <param name="Mappings">The Book's explicit one-to-one chapter associations.</param>
public sealed record BookChapterMappingsResponse(IReadOnlyList<BookChapterAudioMapping> Mappings);

/// <summary>
/// Replaces all explicit audiobook-to-readable-chapter associations for one Book.
/// </summary>
/// <param name="Mappings">The complete desired one-to-one chapter map.</param>
public sealed record ReplaceBookChapterMappingsRequest(IReadOnlyList<BookChapterAudioMapping> Mappings);
