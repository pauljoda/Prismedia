using Prismedia.Contracts.Books;

namespace Prismedia.Application.Books;

/// <summary>
/// Reads compact navigation metadata for a book without streaming its source file to the client.
/// </summary>
public interface IBookContentsService {
    /// <summary>
    /// Gets the authorized book's table of contents and reading-order ranges.
    /// </summary>
    /// <param name="bookId">Identifier of the book Entity.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Compact contents, or <c>null</c> when the book has no readable EPUB source.</returns>
    Task<BookContentsResponse?> GetAsync(Guid bookId, CancellationToken cancellationToken);
}
