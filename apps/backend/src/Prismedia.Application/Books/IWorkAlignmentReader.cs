using Prismedia.Domain.Media.Books;

namespace Prismedia.Application.Books;

/// <summary>
/// Loads the current reading/listening alignment of a work: its readable chapter windows, its
/// playable tracks and audio chapter windows, and the persisted chapter pairings. Both the alignment
/// projection and the listening write path use it, so they always agree on where a position lies.
/// </summary>
public interface IWorkAlignmentReader {
    #region Actions - Loading

    /// <summary>Loads the alignment of one work.</summary>
    /// <param name="workId">Identifier of the work Entity.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The alignment, or <c>null</c> when the work does not exist.</returns>
    Task<WorkAlignment?> LoadAsync(Guid workId, CancellationToken cancellationToken);

    #endregion
}
