using Prismedia.Contracts.Entities;

namespace Prismedia.Application.Entities;

/// <summary>Counts the identification backlog per kind in one read, under the caller's library and NSFW visibility.</summary>
public interface IUnidentifiedEntityCounter {
    #region Abstract Methods

    /// <summary>
    /// Counts unorganized items that have source media and are not wanted placeholders, grouped by kind.
    /// </summary>
    /// <param name="kind">Comma-separated kind codes to count, or null for every kind.</param>
    /// <param name="hideNsfw">Whether NSFW items are excluded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One count per kind that has at least one unidentified item.</returns>
    Task<IReadOnlyList<UnidentifiedKindCount>> CountUnidentifiedAsync(string? kind, bool hideNsfw, CancellationToken cancellationToken);

    #endregion
}
