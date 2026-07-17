using Prismedia.Contracts.System;

namespace Prismedia.Application.Updates;

/// <summary>
/// Reports whether a newer Prismedia build has been published for this host's release
/// channel. Implemented in Infrastructure against the container registry; the API serves
/// the result to the Svelte shell without blocking on registry availability.
/// </summary>
public interface IUpdateCheckService {
    /// <summary>
    /// Returns the current update status, served from a short-lived cache unless
    /// <paramref name="force"/> requests a fresh registry check.
    /// </summary>
    /// <param name="force">Bypass the cached status and query the registry now.</param>
    /// <param name="cancellationToken">Cancels the registry lookup.</param>
    /// <returns>The channel, local/latest versions, and availability status.</returns>
    Task<UpdateCheckResponse> CheckAsync(bool force, CancellationToken cancellationToken);
}
