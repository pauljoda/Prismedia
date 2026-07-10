namespace Prismedia.Application.Entities;

/// <summary>
/// Resolves canonical physical source ownership through the Entity parent/child hierarchy. A wrapper
/// Entity is source-backed when it or any structural descendant owns a Source file.
/// </summary>
public interface IEntitySourceOwnershipReader {
    /// <summary>Returns the requested Entity ids whose structural subtrees own source media.</summary>
    Task<IReadOnlySet<Guid>> ResolveAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken);
}
