using Prismedia.Contracts.Jobs;

namespace Prismedia.Application.Jobs;

/// <summary>Reads how much background work each job type did per hour over a recent window.</summary>
public interface IJobActivityReader {
    #region Abstract Methods

    /// <summary>
    /// Buckets every run that finished, started, or was queued inside the window by job type and hour.
    /// Running work counts in the current hour so a long scan reads as live.
    /// </summary>
    /// <param name="hours">Window length in hours, ending now.</param>
    /// <param name="hideNsfw">Whether runs targeting NSFW items or libraries are excluded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<JobActivityResponse> ListActivityAsync(int hours, bool hideNsfw, CancellationToken cancellationToken);

    #endregion
}
