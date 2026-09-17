using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Connection aggregate plus write-only credential presence, without exposing credential values.</summary>
public sealed record StoredIntegrationConnection(IntegrationConnection Connection, IReadOnlyList<string> ConfiguredSecretKeys);

/// <summary>Persistence boundary for connections; implementations protect secrets and enforce optimistic concurrency.</summary>
public interface IIntegrationConnectionStore {
    /// <summary>Lists independently configured connections without loading plaintext secrets.</summary>
    Task<IReadOnlyList<StoredIntegrationConnection>> ListAsync(CancellationToken cancellationToken);
    /// <summary>Loads one connection for a command.</summary>
    Task<StoredIntegrationConnection?> FindAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Atomically persists a new revision and explicit secret updates. Null expected revision creates.</summary>
    Task SaveAsync(IntegrationConnection connection, long? expectedRevision, IReadOnlyDictionary<string, string?> secretChanges, CancellationToken cancellationToken);
    /// <summary>Decrypts only the requested, currently declared keys at an authorized invocation boundary; leaves all other encrypted values untouched.</summary>
    Task<IReadOnlyDictionary<string, string>> ReadSecretsAsync(Guid id, IReadOnlyCollection<string> credentialKeys, CancellationToken cancellationToken);
    /// <summary>Removes an unused connection at the expected revision.</summary>
    Task DeleteAsync(Guid id, long expectedRevision, CancellationToken cancellationToken);
}

/// <summary>The caller attempted to replace newer connection configuration or health.</summary>
public sealed class ConnectionConflictException() : Exception("The connection changed. Reload it before retrying.");

/// <summary>The selected connection no longer exists.</summary>
public sealed class ConnectionNotFoundException() : Exception("The connection was not found.");

/// <summary>Persistent encryption keys are unavailable; stored secrets must be recovered or replaced.</summary>
public sealed class ConnectionSecretUnavailableException() : Exception("Connection credentials could not be decrypted. Restore the key directory or enter the credentials again.");

/// <summary>Transfer evidence still refers to this connection, so its identity must be retained.</summary>
public sealed class ConnectionInUseException() : Exception("This connection has retained transfers, requests, managed library records, or mapped libraries. Disable it to stop new work while preserving its records and file protection.");
