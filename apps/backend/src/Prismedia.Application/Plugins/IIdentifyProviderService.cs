using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Plugins;

/// <summary>
/// Application-facing provider selector and transient identify orchestration.
/// </summary>
public interface IIdentifyProviderService {
    /// <summary>Lists enabled providers that can identify the requested entity kind.</summary>
    Task<IReadOnlyList<PluginProvider>> ListProvidersAsync(string? entityKind, CancellationToken cancellationToken);

    /// <summary>Runs one transient provider lookup for an entity.</summary>
    Task<IdentifyPluginResponse> IdentifyAsync(
        Guid entityId,
        string providerId,
        IdentifyQuery? query,
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
