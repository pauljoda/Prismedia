namespace Prismedia.Application.Integrations;

/// <summary>Publishes a transfer run using the same persistence transaction as accepted intent.</summary>
public interface IIntegrationTransferScheduler {
    #region Abstract Methods

    /// <summary>Schedules the stable operation; repeated dispatch must deduplicate an active run.</summary>
    Task EnqueueAsync(Guid operationId, string title, CancellationToken cancellationToken);

    #endregion
}
