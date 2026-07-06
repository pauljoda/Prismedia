namespace Prismedia.Application.Security;

/// <summary>
/// Read/write port for per-user library access grants, extending the read side used by
/// the current-user context. Admins are never granted rows — they bypass the table.
/// </summary>
public interface ILibraryAccessStore : ILibraryAccessReader {
    /// <summary>User ids granted to each library root (roots with no grants are absent).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAccessByRootAsync(CancellationToken cancellationToken);

    /// <summary>Library root ids granted to each user (users with no grants are absent).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAccessByUserAsync(CancellationToken cancellationToken);

    /// <summary>Replaces the set of users granted to one library root.</summary>
    Task ReplaceRootAccessAsync(Guid libraryRootId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Replaces the set of library roots granted to one user.</summary>
    Task ReplaceUserAccessAsync(Guid userId, IReadOnlyCollection<Guid> libraryRootIds, CancellationToken cancellationToken);

    /// <summary>Adds grants for one library root without disturbing existing ones.</summary>
    Task GrantRootAccessAsync(Guid libraryRootId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}
