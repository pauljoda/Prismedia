using Prismedia.Contracts.Entities;

namespace Prismedia.Application.Entities;

/// <summary>Outcome category for user-managed taxonomy create/delete use cases.</summary>
public enum EntityCommandStatus {
    /// <summary>The entity was created.</summary>
    Created,

    /// <summary>The entity was deleted.</summary>
    Deleted,

    /// <summary>No active entity of the requested kind and identifier exists.</summary>
    NotFound,

    /// <summary>The request was structurally valid but invalid for the domain (e.g. a blank title).</summary>
    Invalid,

    /// <summary>The requested kind is not a user-manageable taxonomy kind.</summary>
    KindNotManageable
}

/// <summary>Result of creating a taxonomy entity.</summary>
/// <param name="Status">Command outcome.</param>
/// <param name="Id">Identifier of the created entity when successful.</param>
/// <param name="Message">Human-readable error detail when unsuccessful.</param>
public sealed record EntityCreateResult(EntityCommandStatus Status, Guid? Id = null, string? Message = null);

/// <summary>Result of deleting a taxonomy entity.</summary>
/// <param name="Status">Command outcome.</param>
/// <param name="Message">Human-readable error detail when unsuccessful.</param>
public sealed record EntityDeleteResult(EntityCommandStatus Status, string? Message = null);

/// <summary>
/// Application port for creating and deleting the user-managed taxonomy entities (tags, people,
/// studios). Media-derived kinds such as videos or galleries are produced by scans and are not
/// manageable through this port.
/// </summary>
public interface IEntityManagementService {
    /// <summary>
    /// Creates a new taxonomy entity of the given kind from a title the user typed.
    /// </summary>
    /// <param name="kind">Stable kind code; must be a manageable taxonomy kind.</param>
    /// <param name="request">Create request carrying the title and NSFW flag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created entity identifier, or a failure status.</returns>
    Task<EntityCreateResult> CreateAsync(
        string kind,
        EntityCreateRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one taxonomy entity and detaches it from any media that referenced it, so the
    /// entity disappears from grids and no media keeps a dangling link to it.
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="kind">Stable kind code; must be a manageable taxonomy kind and match the entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The delete outcome for HTTP status mapping.</returns>
    Task<EntityDeleteResult> DeleteAsync(
        Guid id,
        string kind,
        CancellationToken cancellationToken);
}
