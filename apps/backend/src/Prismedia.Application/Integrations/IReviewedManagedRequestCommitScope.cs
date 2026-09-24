namespace Prismedia.Application.Integrations;

/// <summary>Runs local wanted materialization and durable manager acceptance in one short persistence transaction.</summary>
public interface IReviewedManagedRequestCommitScope {
    #region Abstract Methods

    /// <summary>Locks and revision-fences the manager connection, then executes a fully preflighted local mutation atomically.</summary>
    Task<T> ExecuteAsync<T>(
        Guid connectionId,
        long expectedConnectionRevision,
        Func<CancellationToken, Task<T>> action,
        CancellationToken token);

    #endregion
}
