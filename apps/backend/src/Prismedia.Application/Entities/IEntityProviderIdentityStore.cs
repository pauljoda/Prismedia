using Prismedia.Domain.Entities;

namespace Prismedia.Application.Entities;

/// <summary>The exact plugin route selected as an Entity's metadata/monitoring identity.</summary>
public sealed record EntityProviderIdentityBinding(
    Guid EntityId,
    string PluginId,
    ExternalIdentity Identity);

/// <summary>Persists authoritative Entity-to-plugin identity routes independently of plugin installs.</summary>
public interface IEntityProviderIdentityStore {
    /// <summary>Loads the binding, or null when the Entity has not selected one.</summary>
    Task<EntityProviderIdentityBinding?> GetAsync(Guid entityId, CancellationToken cancellationToken);

    /// <summary>Creates or replaces the Entity's exact plugin identity route.</summary>
    Task SetAsync(
        Guid entityId,
        string pluginId,
        ExternalIdentity identity,
        CancellationToken cancellationToken);
}
