using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>
/// Prepares one exact target inside a holding the connected manager already has, such as a missing issue
/// of a run. Implementations are discovered by the holding's Entity kind, so the reviewed intake never
/// branches on particular kinds.
/// </summary>
public interface IManagedConnectedTargetPreparer {
    #region Variables

    /// <summary>Entity kind of the connected holding this preparer reads targets from.</summary>
    EntityKind Kind { get; }

    #endregion

    #region Abstract Methods

    /// <summary>
    /// Derives the exact work and target identities from the manager's current snapshot without writing.
    /// </summary>
    /// <exception cref="ArgumentException">The target is not in the snapshot, changed, or already has a file.</exception>
    ReviewedConnectedTarget Review(ManagedItemSnapshot snapshot, ManagedConnectedTargetInput input);

    /// <summary>Materializes the local work and target and reports whether the target already has a file.</summary>
    Task<ManagedWantedWork> PrepareAsync(ReviewedConnectedTarget reviewed, CancellationToken token);

    #endregion
}
