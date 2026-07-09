using Prismedia.Domain.Entities;

namespace Prismedia.Application.Entities;

/// <summary>
/// Describes how an external-identity write reconciles incoming namespaces with identities already
/// attached to an entity.
/// </summary>
public enum ExternalIdentityWriteMode {
    /// <summary>Adds only namespaces the entity does not already carry.</summary>
    AddMissing,

    /// <summary>Adds new namespaces and updates the value and URL of existing namespaces.</summary>
    Upsert,

    /// <summary>
    /// Makes the persisted namespace set equal to the supplied set by adding, updating, and removing
    /// identities as needed.
    /// </summary>
    ReplaceAll
}

/// <summary>Outcome derived from the number of distinct local entities matching external identities.</summary>
public enum ExternalIdentityResolutionStatus {
    /// <summary>No local entity matched any supplied identity.</summary>
    NotFound,

    /// <summary>Every matching identity converged on one local entity.</summary>
    Matched,

    /// <summary>Matching identities resolved to more than one local entity.</summary>
    Ambiguous
}

/// <summary>
/// One local entity found during external-identity resolution and the identities that matched it.
/// </summary>
/// <param name="EntityId">Local entity identifier.</param>
/// <param name="MatchedIdentities">External identities that provided evidence for this match.</param>
public sealed record ExternalIdentityMatch(
    Guid EntityId,
    IReadOnlyList<ExternalIdentity> MatchedIdentities);

/// <summary>
/// Complete external-identity resolution result. Status is derived from the distinct local matches so
/// callers cannot accidentally treat an ambiguous result as a single match.
/// </summary>
public sealed record ExternalIdentityResolution {
    /// <summary>Creates a resolution from every distinct local entity match.</summary>
    /// <param name="matches">Distinct local matches and the identity evidence for each one.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="matches"/> is null.</exception>
    public ExternalIdentityResolution(IReadOnlyList<ExternalIdentityMatch> matches) {
        ArgumentNullException.ThrowIfNull(matches);
        Matches = matches;
    }

    /// <summary>Every distinct local entity matched by the supplied external identities.</summary>
    public IReadOnlyList<ExternalIdentityMatch> Matches { get; }

    /// <summary>Status derived from <see cref="Matches"/>.</summary>
    public ExternalIdentityResolutionStatus Status => Matches.Count switch {
        0 => ExternalIdentityResolutionStatus.NotFound,
        1 => ExternalIdentityResolutionStatus.Matched,
        _ => ExternalIdentityResolutionStatus.Ambiguous
    };

    /// <summary>
    /// The one matched entity identifier, or null when resolution found none or remained ambiguous.
    /// </summary>
    public Guid? EntityId => Matches.Count == 1 ? Matches[0].EntityId : null;
}

/// <summary>
/// Application persistence port for canonical external identities attached to local entities. The
/// implementation participates in the caller's unit of work and never commits it.
/// </summary>
public interface IEntityExternalIdentityStore {
    /// <summary>Lists the canonical external identities attached to one local entity.</summary>
    /// <param name="entityId">Local entity identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the database operation.</param>
    /// <returns>Canonical identity associations in stable persistence order.</returns>
    Task<IReadOnlyList<EntityExternalId>> ListAsync(
        Guid entityId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a set of identities against local entities of one kind. When
    /// <paramref name="parentEntityId"/> is supplied, only children of that structural parent match;
    /// null leaves parent placement unconstrained.
    /// </summary>
    /// <param name="kind">Required local entity kind.</param>
    /// <param name="identities">Canonical external identities to resolve as one evidence set.</param>
    /// <param name="parentEntityId">Optional structural parent scope.</param>
    /// <param name="cancellationToken">Token used to cancel the database operation.</param>
    /// <returns>All distinct local matches and a status derived from their count.</returns>
    Task<ExternalIdentityResolution> ResolveAsync(
        EntityKind kind,
        IReadOnlyCollection<ExternalIdentity> identities,
        Guid? parentEntityId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reconciles canonical identities for one entity without saving the caller's unit of work.
    /// </summary>
    /// <param name="entityId">Local entity receiving the identities.</param>
    /// <param name="identities">Canonical identity associations to write.</param>
    /// <param name="mode">Reconciliation behavior for existing namespaces.</param>
    /// <param name="cancellationToken">Token used to cancel the database operation.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the input contains different values for the same normalized namespace.
    /// </exception>
    Task WriteAsync(
        Guid entityId,
        IReadOnlyCollection<EntityExternalId> identities,
        ExternalIdentityWriteMode mode,
        CancellationToken cancellationToken);
}
