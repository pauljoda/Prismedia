using Prismedia.Domain.Entities;

namespace Prismedia.Application.Plugins;

/// <summary>
/// A plugin capable of handling one persistent external identity. The plugin installation id and
/// external identity namespace are deliberately separate: multiple plugins may handle the same
/// namespace, and one plugin may handle several namespaces.
/// </summary>
/// <param name="PluginId">Stable plugin manifest id selected to handle the identity.</param>
/// <param name="Identity">Persistent upstream identity being handled.</param>
public sealed record PluginIdentityRoute(string PluginId, ExternalIdentity Identity);

/// <summary>
/// Resolves persistent identities to enabled plugins by entity kind and identify action.
/// Implementations return every valid route in deterministic order, so shared-namespace ambiguity
/// is explicit and callers can try routes without silently binding an identity to an arbitrary plugin.
/// </summary>
public interface IPluginIdentityRouter {
    /// <summary>
    /// Resolves all enabled plugin routes for the supplied identities, kind, and required action.
    /// Duplicate routes are collapsed; an empty list means no installed plugin can handle them.
    /// </summary>
    Task<IReadOnlyList<PluginIdentityRoute>> ResolveAsync(
        string entityKindCode,
        IdentifyAction action,
        IReadOnlyList<ExternalIdentity> identities,
        CancellationToken cancellationToken);
}
