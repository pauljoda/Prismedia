namespace Prismedia.Application.Security;

/// <summary>
/// Read port for per-user library access grants. Backing storage arrives with the
/// permissions slice; the current-user context tolerates the port being unregistered
/// (treated as unrestricted) so auth can ship first.
/// </summary>
public interface ILibraryAccessReader {
    /// <summary>Library root ids the given member user may see.</summary>
    Task<IReadOnlySet<Guid>> GetAllowedRootIdsAsync(Guid userId, CancellationToken cancellationToken);
}
