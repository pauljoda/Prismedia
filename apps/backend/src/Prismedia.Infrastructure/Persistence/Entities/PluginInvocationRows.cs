namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>Deployment-wide admission clock for one installed plugin, independent of queue history and connection instances.</summary>
public sealed class PluginInvocationStateRow {
    public string PluginId { get; set; } = string.Empty;
    public DateTimeOffset NextStartAt { get; set; }
}

/// <summary>One bounded child-process slot; abandoned slots expire after the host's maximum invocation lifetime.</summary>
public sealed class PluginInvocationLeaseRow {
    public Guid Id { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
