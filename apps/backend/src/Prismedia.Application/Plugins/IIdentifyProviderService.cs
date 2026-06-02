using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Plugins;

/// <summary>
/// Application-facing provider selector and transient identify orchestration.
/// </summary>
public interface IIdentifyProviderService {
    /// <summary>Lists enabled providers that can identify the requested entity kind.</summary>
    Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(string? entityKind, CancellationToken cancellationToken);

    /// <summary>Runs one transient provider lookup for an entity.</summary>
    /// <param name="parentExternalIds">
    /// Optional just-resolved provider IDs of the entity's parent (from an unsaved parent proposal),
    /// merged into the immediate ancestor snapshot so a plugin can resolve the child in its parent's
    /// context. Null for a normal standalone identify.
    /// </param>
    Task<IdentifyPluginResponse> IdentifyAsync(
        Guid entityId,
        string providerId,
        IdentifyQuery? query,
        IReadOnlyDictionary<string, string>? parentExternalIds,
        bool hideNsfw,
        CancellationToken cancellationToken);

    /// <summary>Applies selected metadata proposal fields to an entity.</summary>
    Task<bool> ApplyAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        IReadOnlyCollection<string> selectedFields,
        IReadOnlyDictionary<string, string?>? selectedImages,
        CancellationToken cancellationToken);
}
