using Prismedia.Contracts.Entities;

namespace Prismedia.Application.Entities;

/// <summary>
/// Outcome of applying editable metadata to an entity.
/// </summary>
public enum EntityMetadataPatchResult {
    /// <summary>The entity existed and the requested metadata fields were applied.</summary>
    Applied,

    /// <summary>No active entity with the requested identifier exists.</summary>
    NotFound,

    /// <summary>The entity exists but does not match the route's expected entity kind.</summary>
    KindMismatch
}

/// <summary>
/// Application port for applying capability-shaped metadata updates to entities.
/// </summary>
public interface IEntityMetadataPatchService {
    /// <summary>
    /// Applies an editable metadata patch to one active entity.
    /// </summary>
    /// <param name="entityId">Entity receiving metadata.</param>
    /// <param name="request">Field-scoped patch request.</param>
    /// <param name="expectedKind">Optional route kind guard. Null disables kind checking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Patch outcome for HTTP status mapping.</returns>
    Task<EntityMetadataPatchResult> ApplyPatchAsync(
        Guid entityId,
        EntityMetadataUpdateRequest request,
        string? expectedKind,
        CancellationToken cancellationToken);
}
