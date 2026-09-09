using Microsoft.Extensions.Logging;

namespace Prismedia.Application.Acquisition;

/// <summary>A retained attempt whose independently acquired replacement has finished importing.</summary>
public sealed record SupersededHeldAcquisition(Guid HeldId, Guid ReplacementId);

/// <summary>Elects cleanup only after current library ownership and held-payload authority are checked together.</summary>
public interface ISupersededHeldAcquisitionStore {
    /// <summary>Lists completed alternatives, including interrupted cleanup claims.</summary>
    Task<IReadOnlyList<SupersededHeldAcquisition>> ListAsync(CancellationToken cancellationToken);
    /// <summary>Claims ordinary durable removal while the replacement is still present and the retained attempt remains disposable.</summary>
    Task<bool> TryClaimAsync(SupersededHeldAcquisition candidate, CancellationToken cancellationToken);
}

/// <summary>Removes superseded held transfers through normal ownership-aware downloader teardown.</summary>
public sealed class SupersededHeldAcquisitionCleanup(ISupersededHeldAcquisitionStore store,
    IAcquisitionRequestService requests, ILogger<SupersededHeldAcquisitionCleanup> logger, SupersededHeldAcquisitionCursor cursor) {
    /// <summary>Retries interrupted removal without turning failed replacements into authority to discard retained files.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken) {
        foreach (var candidate in cursor.Next(await store.ListAsync(cancellationToken))) {
            cursor.Advance(candidate.HeldId);
            try {
                if (await store.TryClaimAsync(candidate, cancellationToken))
                    await requests.DeleteAsync(candidate.HeldId, cancellationToken);
            } catch (OperationCanceledException) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "Could not clean up superseded held acquisition {Id}.", candidate.HeldId); }
        }
    }
}

/// <summary>Rotates bounded cleanup work so unavailable downloaders or retained extras cannot starve later holds.</summary>
public sealed class SupersededHeldAcquisitionCursor {
    private Guid last;
    /// <summary>Returns at most eight holds in stable, rotating order.</summary>
    public IReadOnlyList<SupersededHeldAcquisition> Next(IReadOnlyList<SupersededHeldAcquisition> candidates) =>
        candidates.OrderBy(candidate => candidate.HeldId.CompareTo(last) <= 0).ThenBy(candidate => candidate.HeldId).Take(8).ToArray();
    /// <summary>Advances after considering a hold, whether or not cleanup was possible.</summary>
    public void Advance(Guid id) => last = id;
}
