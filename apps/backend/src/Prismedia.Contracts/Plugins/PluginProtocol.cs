namespace Prismedia.Contracts.Plugins;

/// <summary>
/// Canonical version of the request/response protocol shared by Prismedia and external plugin
/// processes. Plugin manifests express compatible protocol ranges as semantic versions, while each
/// process request carries the matching major integer version.
/// </summary>
public static class PluginProtocol {
    /// <summary>Current major protocol version written into every plugin process request.</summary>
    public const int CurrentVersion = 2;

    /// <summary>Current semantic protocol version evaluated against plugin manifest compatibility bounds.</summary>
    public const string CurrentSemanticVersion = "2.0.0";
}
