namespace Prismedia.Application.Health;

/// <summary>
/// Stable identifier shared by hosted worker services for the current process lifetime.
/// </summary>
public sealed class WorkerRuntimeIdentity {
    /// <summary>
    /// Worker identifier used in heartbeats and durable queue leases.
    /// </summary>
    public string WorkerId { get; } = $"{Environment.MachineName}-{Guid.NewGuid():N}";
}
