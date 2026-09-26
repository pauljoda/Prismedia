using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Persistence boundary for connections; implementations protect secrets and enforce optimistic concurrency.</summary>
public interface IIntegrationConnectionStore {
    #region Abstract Methods

    /// <summary>Lists independently configured connections without loading plaintext secrets.</summary>
    Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Loads one connection for a command.</summary>
    Task<StoredIntegrationConnection?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Atomically persists a new revision and explicit secret updates. Null expected revision creates.</summary>
    Task SaveAsync(IntegrationConnection connection, long? expectedRevision, IReadOnlyDictionary<string, string?> secretChanges,
        CancellationToken cancellationToken);

    /// <summary>Decrypts only the requested, currently declared keys at an authorized invocation boundary; leaves all other
    /// encrypted values untouched.</summary>
    Task<IReadOnlyDictionary<string, string>> ReadSecretsAsync(Guid id, IReadOnlyCollection<string> credentialKeys,
        CancellationToken cancellationToken);

    /// <summary>Removes an unused connection at the expected revision.</summary>
    Task DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken);

    #endregion
}
