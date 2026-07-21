using Microsoft.Extensions.Logging;
using Prismedia.Application.Requests;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Converts a barren whole-unit search into immediate direct-child acquisition intent. The periodic
/// monitor sweep remains the durable recovery net; this coordinator removes its interval from the
/// normal request path so an album with no acceptable release starts track searches immediately.
/// </summary>
public sealed class AcquisitionMissingChildFallback(
    IMissingChildAcquisitionRequester requests,
    ILogger<AcquisitionMissingChildFallback> logger) {
    /// <summary>
    /// Requests wanted children when <paramref name="input"/> is a structural unit linked to an Entity
    /// and <paramref name="outcome"/> contains no acceptable whole-unit release. Returns null when no
    /// fallback applies or when the periodic monitor sweep must recover a best-effort failure.
    /// </summary>
    public async Task<(int Covered, int Missing)?> TryStartAsync(
        AcquisitionSearchInput input,
        AcquisitionSearchOutcome outcome,
        CancellationToken cancellationToken) {
        if (input.EntityId is not { } entityId
            || outcome.Candidates.Any(candidate => candidate.Accepted)
            || RequestKindRegistry.FindChildMaterializingUnit(input.Kind) is null) {
            return null;
        }

        try {
            return await requests.RequestMissingChildrenAsync(entityId, cancellationToken);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception exception) {
            logger.LogWarning(
                exception,
                "Acquisition {AcquisitionId} could not start its missing-child fallback; the monitor sweep will retry it.",
                input.Id);
            return null;
        }
    }
}
