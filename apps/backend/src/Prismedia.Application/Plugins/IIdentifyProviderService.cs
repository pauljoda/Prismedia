using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Plugins;

/// <summary>
/// Application-facing provider selector and transient identify orchestration.
/// </summary>
public interface IIdentifyProviderService {
    /// <summary>Lists enabled providers that can identify the requested entity kind.</summary>
    Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(string? entityKind, CancellationToken cancellationToken);

    /// <summary>Runs one provider lookup for an entity, optionally cascading into its full child tree.</summary>
    /// <param name="parentExternalIds">
    /// Optional just-resolved provider IDs of the entity's parent (from an unsaved parent proposal),
    /// merged into the immediate ancestor snapshot so a plugin can resolve the child in its parent's
    /// context. Null for a normal standalone identify.
    /// </param>
    /// <param name="cascadeChildren">
    /// When true (default) the entity's local structural children are recursively identified and the
    /// full tree is returned. When false only the entity plus whatever children the provider returns
    /// in its own proposal are resolved — used to seed a queue item quickly before the streaming
    /// cascade job fills in the rest.
    /// </param>
    /// <param name="sink">
    /// Optional streaming sink invoked with the growing root proposal after each top-level child
    /// resolves, so a long cascade can publish partial results. Only the root level flushes.
    /// </param>
    Task<IdentifyPluginResponse> IdentifyAsync(
        Guid entityId,
        string providerId,
        IdentifyQuery? query,
        IReadOnlyDictionary<string, string>? parentExternalIds,
        bool hideNsfw,
        CancellationToken cancellationToken,
        bool cascadeChildren = true,
        IIdentifyCascadeSink? sink = null);

    /// <summary>Applies selected metadata proposal fields to an entity.</summary>
    Task<bool> ApplyAsync(
        Guid entityId,
        EntityMetadataProposal proposal,
        IReadOnlyCollection<string> selectedFields,
        IReadOnlyDictionary<string, string?>? selectedImages,
        CancellationToken cancellationToken,
        IIdentifyApplyProgressReporter? progress = null);
}
