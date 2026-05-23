namespace Prismedia.Contracts.Plugins;

/// <summary>
/// Identity hints sent to a plugin for ID-first metadata lookup.
/// </summary>
/// <param name="ExternalIds">Provider-specific IDs already attached to the entity.</param>
/// <param name="Urls">Entity URLs that may contain provider IDs.</param>
/// <param name="Title">Current Prismedia entity title used as the search fallback.</param>
/// <param name="FilePath">Primary source file path when available.</param>
public sealed record IdentifyMatchHints(
    IReadOnlyDictionary<string, string> ExternalIds,
    IReadOnlyList<string> Urls,
    string? Title,
    string? FilePath);

/// <summary>
/// Minimal entity snapshot passed to plugins.
/// </summary>
/// <param name="Id">Prismedia entity identifier.</param>
/// <param name="Kind">Prismedia entity kind code.</param>
/// <param name="Title">Current title.</param>
/// <param name="ExternalIds">Provider-specific identities already attached to the entity.</param>
/// <param name="Urls">Entity URLs that may carry provider identity.</param>
public sealed record IdentifyEntitySnapshot(
    Guid Id,
    string Kind,
    string Title,
    IReadOnlyDictionary<string, string>? ExternalIds = null,
    IReadOnlyList<string>? Urls = null);

/// <summary>
/// Structural context for a plugin identify request.
/// </summary>
/// <param name="Ancestors">Structural ancestor entities from immediate parent outward.</param>
/// <param name="Positions">Known generic ordering/position values for the current entity.</param>
public sealed record IdentifyStructuralContext(
    IReadOnlyList<IdentifyEntitySnapshot> Ancestors,
    IReadOnlyDictionary<string, int> Positions);

/// <summary>
/// User-entered identify query overrides.
/// </summary>
/// <param name="Title">Optional title/query override.</param>
/// <param name="Url">Optional provider URL override.</param>
/// <param name="ExternalIds">Optional explicit provider IDs, usually from candidate picks.</param>
public sealed record IdentifyQuery(
    string? Title,
    string? Url,
    IReadOnlyDictionary<string, string>? ExternalIds);

/// <summary>
/// Request envelope sent to short-lived plugin processes.
/// </summary>
/// <param name="ProtocolVersion">Plugin protocol version expected by Prismedia.</param>
/// <param name="Action">Action code requested by Prismedia.</param>
/// <param name="Auth">Resolved credential values for the plugin.</param>
/// <param name="Entity">Entity snapshot being identified.</param>
/// <param name="Query">Optional user-provided query override.</param>
/// <param name="Hints">ID-first lookup hints derived from existing entity metadata.</param>
/// <param name="StructuralContext">Structural parent and position context for child-aware identify.</param>
public sealed record IdentifyPluginRequest(
    int ProtocolVersion,
    string Action,
    IReadOnlyDictionary<string, string> Auth,
    IdentifyEntitySnapshot Entity,
    IdentifyQuery Query,
    IdentifyMatchHints Hints,
    IdentifyStructuralContext? StructuralContext = null);

/// <summary>
/// Request body for identifying one entity with a provider.
/// </summary>
public sealed record IdentifyEntityRequest(string Provider, IdentifyQuery? Query);

/// <summary>
/// Request body for starting a transient bulk identify review session.
/// </summary>
public sealed record IdentifyBulkStartRequest(
    string Provider,
    IReadOnlyList<Guid> EntityIds,
    IdentifyQuery? Query);
