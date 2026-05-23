namespace Prismedia.Application.Organization;

/// <summary>
/// Application port for organize-plan persistence operations. The planner asks the
/// implementation for the inputs it needs to compute a move plan, and applies the
/// post-move path-prefix rewrite through this port. All filesystem move operations
/// happen in the Application service itself.
/// </summary>
public interface IOrganizePersistence {
    /// <summary>
    /// Lists watched library roots, optionally filtered to one root by identifier.
    /// </summary>
    Task<IReadOnlyList<OrganizeLibraryRoot>> ListRootsAsync(Guid? rootId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists active (non-deleted) entities, optionally filtered to one entity by identifier.
    /// </summary>
    Task<IReadOnlyList<OrganizeEntityRow>> ListActiveEntitiesAsync(Guid? entityId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the canonical source file (role = source) for each entity in <paramref name="entityIds"/>.
    /// Entities without a source file are simply absent from the result.
    /// </summary>
    Task<IReadOnlyList<OrganizeSourceFile>> ListSourceFilesAsync(IReadOnlyCollection<Guid> entityIds, CancellationToken cancellationToken);

    /// <summary>
    /// Rewrites stored source-file and source-capability path values for one applied move.
    /// Updates any rows whose path equals <paramref name="sourcePath"/> or is rooted under it,
    /// translating them to the equivalent path under <paramref name="targetPath"/>, then commits.
    /// </summary>
    Task ApplyPathPrefixRewriteAsync(string sourcePath, string targetPath, CancellationToken cancellationToken);
}
