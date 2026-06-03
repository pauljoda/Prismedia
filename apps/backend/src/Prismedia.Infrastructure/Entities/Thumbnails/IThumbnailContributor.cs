namespace Prismedia.Infrastructure.Entities.Thumbnails;

/// <summary>
/// Augments the shared thumbnail projection with extra, kind-scoped data without bloating the base
/// list query. Every registered contributor runs once per page after the core projection is built;
/// each one self-filters to the kinds it cares about and issues at most one batched query, so a
/// media grid never pays for a taxonomy lookup and vice versa. New thumbnail extras are added by
/// implementing this interface — it is discovered by DI, so no call-site wiring is required.
/// </summary>
public interface IThumbnailContributor {
    /// <summary>
    /// Inspects the page's rows and writes any extra meta chips or reference counts onto
    /// <paramref name="contributions"/>. Implementations must be a no-op when none of the rows are
    /// relevant to keep media-only pages free of extra queries.
    /// </summary>
    /// <param name="contributions">Accumulator carrying the page's rows and collecting results.</param>
    /// <param name="cancellationToken">Token to cancel the contributor's query.</param>
    Task ContributeAsync(ThumbnailContributions contributions, CancellationToken cancellationToken);
}
