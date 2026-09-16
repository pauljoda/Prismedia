using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Persists immutable external library boundaries and verifies local file access without modifying media.</summary>
public interface IExternalLibraryMountStore {
    /// <summary>Lists configured boundaries, including disabled or disconnected libraries.</summary>
    Task<IReadOnlyList<ExternalLibraryMount>> ListAsync(Guid connectionId, CancellationToken token);
    /// <summary>Atomically creates a dedicated root and its boundary after excluding existing libraries and managed work areas.</summary>
    Task<ExternalLibraryMount> CreateAsync(Guid connectionId, long expectedRevision, CreateExternalLibraryMountRequest request, CancellationToken token);
    /// <summary>Checks path containment, readability and length for exactly the remote files observed.</summary>
    Task<IReadOnlyList<MappedLibraryFile>> InspectAsync(Guid connectionId, IReadOnlyList<ManagedLibraryFile> files, CancellationToken token);
}
