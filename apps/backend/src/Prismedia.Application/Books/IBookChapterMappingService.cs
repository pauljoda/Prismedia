using Prismedia.Contracts.Books;

namespace Prismedia.Application.Books;

/// <summary>
/// Reads and replaces the shared structural map between a Book's readable chapters and audiobook tracks.
/// </summary>
public interface IBookChapterMappingService {
    /// <summary>
    /// Gets a visible Book's explicit chapter map.
    /// </summary>
    /// <param name="bookId">Identifier of the Book Entity.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The map, including an empty map, or <c>null</c> when the Book is unavailable.</returns>
    Task<BookChapterMappingsResponse?> GetAsync(Guid bookId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically replaces a visible Book's complete explicit chapter map.
    /// </summary>
    /// <param name="bookId">Identifier of the Book Entity.</param>
    /// <param name="request">The complete desired map.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The save outcome and persisted map when successful.</returns>
    Task<BookChapterMappingSaveResult> ReplaceAsync(
        Guid bookId,
        ReplaceBookChapterMappingsRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Outcome of replacing a Book's explicit chapter map.</summary>
public enum BookChapterMappingSaveStatus {
    Saved,
    NotFound,
    Invalid
}

/// <summary>
/// Result of replacing a Book's explicit chapter map.
/// </summary>
/// <param name="Status">The outcome category.</param>
/// <param name="Response">The persisted map when saved.</param>
/// <param name="Error">A user-facing validation message when invalid.</param>
public sealed record BookChapterMappingSaveResult(
    BookChapterMappingSaveStatus Status,
    BookChapterMappingsResponse? Response,
    string? Error);
