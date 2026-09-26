using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Persists immutable external library boundaries and verifies local file access without modifying media.</summary>
public interface IExternalLibraryMountStore {
    #region Abstract Methods

    /// <summary>Lists the exact watched-root ids protected by external library mappings.</summary>
    Task<IReadOnlySet<Guid>> ListMountedLibraryRootIdsAsync(CancellationToken token);

    /// <summary>Lists configured boundaries, including disabled or disconnected libraries.</summary>
    Task<IReadOnlyList<ExternalLibraryMount>> ListAsync(Guid connectionId, CancellationToken token);

    /// <summary>Atomically creates a dedicated root and its boundary after excluding existing libraries and managed work areas.</summary>
    Task<ExternalLibraryMount> CreateAsync(Guid connectionId, long expectedRevision, CreateExternalLibraryMountRequest request,
        CancellationToken token);

    /// <summary>Atomically makes one reviewed existing library an external boundary without changing its contents or
    /// configuration.</summary>
    Task<ExternalLibraryMount> AttachAsync(Guid connectionId, long expectedRevision, AttachExistingExternalLibraryMountRequest request,
        CancellationToken token);

    /// <summary>Attaches an existing root and reports whether this call created the immutable boundary.</summary>
    Task<ExternalLibraryMountAttachment> AttachWithResultAsync(Guid connectionId, long expectedRevision,
        AttachExistingExternalLibraryMountRequest request, CancellationToken token);

    /// <summary>Checks path containment, readability and length for exactly the remote files observed.</summary>
    Task<IReadOnlyList<MappedLibraryFile>> InspectAsync(Guid connectionId, IReadOnlyList<ManagedLibraryFile> files,
        CancellationToken token);

    #endregion
}
