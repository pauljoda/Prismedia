namespace Prismedia.Application.Entities;

/// <summary>
/// Removes entity file rows whose generated asset no longer exists on disk (wiped cache volume,
/// manual deletion, historical drift). Keeping rows truthful lets the request path trust the
/// database instead of stat-ing every artwork path on every list page.
/// </summary>
public interface IEntityAssetRowSweeper {
    /// <summary>Deletes rows pointing at missing generated assets; returns the number removed.</summary>
    Task<int> SweepAsync(CancellationToken cancellationToken);
}
