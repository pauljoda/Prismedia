using Prismedia.Contracts.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Finite manager controls, separate from read-only library discovery and byte-transfer executors.</summary>
public interface IIntegrationManagerControlGateway {
    /// <summary>Reads exact scope configuration and optional command history without creating remote intent.</summary>
    Task<ManagedControlState> ReconcileAsync(string pluginId, IntegrationConnectionContext connection, ReconcileManagedInput input, CancellationToken token);
    /// <summary>Applies explicit fields once; caller persists dispatch before invoking and reconciles uncertain results.</summary>
    Task<ManagedMutationResult> ConfigureAsync(string pluginId, IntegrationConnectionContext connection, ConfigureManagedInput input, CancellationToken token);
    /// <summary>Submits one scoped search; caller must never automatically repeat an uncertain submission.</summary>
    Task<ManagedMutationResult> RequestAsync(string pluginId, IntegrationConnectionContext connection, RequestManagedInput input, CancellationToken token);
}
