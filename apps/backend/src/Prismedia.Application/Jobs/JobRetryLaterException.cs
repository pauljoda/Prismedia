namespace Prismedia.Application.Jobs;

/// <summary>
/// Signals that a claimed job should be returned to the queue without consuming an attempt as a hard failure.
/// </summary>
public sealed class JobRetryLaterException : Exception {
    /// <summary>Delay before the job becomes available for another worker pass.</summary>
    public TimeSpan RetryDelay { get; }

    /// <summary>
    /// Creates a retry-later signal with the supplied queue message and retry delay.
    /// </summary>
    /// <param name="message">Queue-visible explanation for why the job was deferred.</param>
    /// <param name="retryDelay">Delay before the job is made available again.</param>
    public JobRetryLaterException(string message, TimeSpan retryDelay) : base(message) {
        RetryDelay = retryDelay;
    }
}
