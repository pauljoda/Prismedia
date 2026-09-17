using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Optional finite source preparation, separate from direct catalog retrieval and durable remote executors.</summary>
public interface IIntegrationSourceAcquisitionGateway {
    /// <summary>Observes exact selection identity and current readiness without a remote mutation.</summary>
    Task<SourceAcquisitionObservation> ObserveSourceAsync(string pluginId, IntegrationConnectionContext connection,
        ObserveSourceInput input, CancellationToken cancellationToken);

    /// <summary>Requests the exact selection after the local intent is persisted. Implementations must coalesce repeated requests without widening or duplicating remote work.</summary>
    Task<SourceAcquisitionObservation> RequestSourceAsync(string pluginId, IntegrationConnectionContext connection,
        RequestSourceInput input, CancellationToken cancellationToken);
}
