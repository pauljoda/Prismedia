using Prismedia.Domain.Entities;

namespace Prismedia.Application.Entities;

/// <summary>
/// Resolves persisted structural progress topology without exposing persistence details to
/// application use cases. Definitions declare the topology; implementations interpret it against
/// the current entity structure.
/// </summary>
public interface IEntityProgressTopologyResolver {
    /// <summary>Resolves the progress owner selected solely by a requested entity identifier.</summary>
    Task<ProgressOwnerResolution?> ResolveOwnerAsync(Guid requestedEntityId, CancellationToken cancellationToken);

    /// <summary>Validates and normalizes a cursor for an already resolved progress owner.</summary>
    Task<ProgressCursorResolution?> ResolveCursorAsync(
        Guid ownerId,
        Guid cursorId,
        CancellationToken cancellationToken);

    /// <summary>Resolves a local work cursor into its absolute position across the declared work.</summary>
    Task<ProgressWorkPosition?> ResolveWorkPositionAsync(
        Guid ownerId,
        Guid cursorId,
        int index,
        int total,
        CancellationToken cancellationToken);

    /// <summary>Resolves all ordered container scopes contributed by an item.</summary>
    Task<IReadOnlyList<OrderedProgressScope>> ResolveOrderedScopesAsync(
        Guid itemId,
        CancellationToken cancellationToken);
}

/// <summary>Resolved owner selected by the requested entity's declared progress topology.</summary>
public sealed record ProgressOwnerResolution(Guid OwnerId);

/// <summary>Validated cursor belonging to a resolved owner, including its normalized cursor id.</summary>
public sealed record ProgressCursorResolution(Guid CursorId, Guid NormalizedCursorId);

/// <summary>Absolute position of a local cursor within its declared work.</summary>
public sealed record ProgressWorkPosition(Guid CursorId, int Index, int Total);

/// <summary>Ordered position of one item within a declared roll-up owner scope.</summary>
public sealed record OrderedProgressScope(
    Guid OwnerId,
    Guid CurrentItemId,
    int Index,
    int Total,
    Guid? NextItemId,
    int CompletedCount = 0);
