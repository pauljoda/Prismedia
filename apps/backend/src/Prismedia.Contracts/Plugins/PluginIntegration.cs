using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Plugins;

/// <summary>Separately versioned operations offered by a plugin package for configured Connections.</summary>
/// <param name="ProtocolVersion">Integration wire version, independent of metadata identify v2.</param>
/// <param name="Capabilities">Optional responsibilities and their supported operations and entity kinds.</param>
/// <param name="Settings">Nonsecret connection settings. Credentials use the manifest Auth schema.</param>
/// <param name="AnonymousArtifactOrigins">Optional HTTPS origins from which acquisition sources may retrieve files without headers or cookies. Executor artifacts remain scoped to their connection.</param>
public sealed record PluginIntegrationDefinition(
    int ProtocolVersion,
    IReadOnlyList<PluginIntegrationCapability> Capabilities,
    IReadOnlyList<PluginSearchField> Settings,
    IReadOnlyList<string>? AnonymousArtifactOrigins = null);

/// <summary>Declared or negotiated support for a single connected-application responsibility.</summary>
public sealed record PluginIntegrationCapability(
    PluginCapability Kind,
    IReadOnlyList<IntegrationOperation> Operations,
    IReadOnlyList<EntityKind> EntityKinds);

/// <summary>Stable values for the integration envelope. Metadata continues to use PluginProtocol.</summary>
public static class IntegrationProtocol {
    /// <summary>Current integration protocol generation.</summary>
    public const int CurrentVersion = 1;
    /// <summary>Discriminator separating connected-application requests from identify requests.</summary>
    public const string Name = "prismedia-integration";
}
