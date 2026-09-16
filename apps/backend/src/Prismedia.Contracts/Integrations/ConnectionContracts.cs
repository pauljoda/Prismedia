using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Integrations;

/// <summary>Public connection configuration and negotiated support. Secret values are never returned.</summary>
public sealed record ConnectionResponse(
    Guid Id, string PluginId, string Name, string BaseUrl, bool Enabled,
    IReadOnlyList<PluginCapability> EnabledCapabilities, IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<string> ConfiguredSecretKeys, long Revision, ConnectionStatus Status,
    string? RemoteInstanceId, bool HasPersistentRemoteIdentity,
    IReadOnlyList<PluginIntegrationCapability> EffectiveCapabilities, DateTimeOffset? LastCheckedAt, string? LastError);

/// <summary>Creates a separate instance of an installed plugin. Secrets are write-only.</summary>
public sealed record CreateConnectionRequest(
    string PluginId, string Name, string BaseUrl, bool Enabled,
    IReadOnlyList<PluginCapability> EnabledCapabilities,
    IReadOnlyDictionary<string, string> Settings, IReadOnlyDictionary<string, string?> Secrets);

/// <summary>Replaces nonsecret configuration; omitted secret keys preserve saved values, null or empty explicitly removes a key.</summary>
public sealed record UpdateConnectionRequest(
    long ExpectedRevision, string Name, string BaseUrl, bool Enabled,
    IReadOnlyList<PluginCapability> EnabledCapabilities,
    IReadOnlyDictionary<string, string> Settings, IReadOnlyDictionary<string, string?> Secrets);

/// <summary>Read-only probe output. A null instance ID explicitly means the upstream lacks persistent installation identity.</summary>
public sealed record ConnectionProbeResult(
    string? InstanceId, string DisplayName, string? Version,
    IReadOnlyList<PluginIntegrationCapability> Capabilities);

/// <summary>Configured context sent only to the trusted plugin process, never to browser clients.</summary>
public sealed record IntegrationConnectionContext(
    Guid Id, string BaseUrl, string? ExpectedInstanceId,
    IReadOnlyDictionary<string, string> Settings, IReadOnlyDictionary<string, string> Auth);

/// <summary>Typed independently versioned request envelope for a short integration operation.</summary>
public sealed record IntegrationPluginRequest<TInput>(
    string Protocol, int ProtocolVersion, Guid InvocationId, IntegrationOperation Operation,
    IntegrationConnectionContext Connection, TInput Input);

/// <summary>Typed result correlated with the exact invocation. Failures must not masquerade as empty success.</summary>
public sealed record IntegrationPluginResponse<TOutput>(
    string Protocol, int ProtocolVersion, Guid InvocationId, bool Ok, TOutput? Result, string? Error);

/// <summary>A connection probe has no operation-specific inputs.</summary>
public sealed record ConnectionProbeInput;
