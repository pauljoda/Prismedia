using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Reads one bounded, connection-aware view over retained fulfillment activity.</summary>
public interface IRequestActivityReader {
    /// <summary>
    /// Returns a deterministic keyset page over current retained state without contacting configured integrations.
    /// A fresh first-page read is the refresh boundary when live state changes between page requests.
    /// </summary>
    /// <param name="connectionId">Optional configured connection boundary.</param>
    /// <param name="cursor">Opaque continuation from the preceding page.</param>
    /// <param name="limit">Requested page size; implementations enforce their public maximum.</param>
    /// <param name="hideNsfw">Whether rows belonging to NSFW library content must be withheld.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<RequestActivityPage> ListAsync(
        Guid? connectionId,
        string? cursor,
        int limit,
        bool hideNsfw,
        CancellationToken cancellationToken);
}
