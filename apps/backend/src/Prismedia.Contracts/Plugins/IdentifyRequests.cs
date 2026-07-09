using Prismedia.Domain.Entities;

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
/// <param name="Kind">Prismedia entity kind.</param>
/// <param name="Title">Current title.</param>
/// <param name="ExternalIds">Provider-specific identities already attached to the entity.</param>
/// <param name="Urls">Entity URLs that may carry provider identity.</param>
public sealed record IdentifyEntitySnapshot(
    Guid Id,
    EntityKind Kind,
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
/// <param name="RequireChoice">When true, keep the queue in candidate-pick mode even if a provider returns a confident proposal.</param>
/// <param name="Fields">
/// Optional plugin-defined search form values keyed by <see cref="PluginSearchField.Key"/>. Values
/// remain strings on the wire and are interpreted according to the selected support's field schema.
/// </param>
public sealed record IdentifyQuery(
    string? Title,
    string? Url,
    IReadOnlyDictionary<string, string>? ExternalIds,
    bool? RequireChoice = null,
    IReadOnlyDictionary<string, string>? Fields = null);

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
/// <param name="IncludeNsfw">Whether plugin lookups may include adult upstream results for this request.</param>
/// <param name="IncludeRelationshipDetails">
/// Whether plugins should eagerly hydrate related entity proposals (people, studios, tags) before
/// returning. Prismedia can set this to false when it will stream relationship hydration itself.
/// </param>
/// <param name="IncludeStructuralChildren">
/// Whether plugins should include structural child proposals (seasons, episodes, book volumes,
/// chapters) in their immediate response. Prismedia can set this to false for seed lookups that
/// should return the root quickly and let a background cascade stream children after the review
/// page opens.
/// </param>
public sealed record IdentifyPluginRequest(
    int ProtocolVersion,
    IdentifyAction Action,
    IReadOnlyDictionary<string, string> Auth,
    IdentifyEntitySnapshot Entity,
    IdentifyQuery Query,
    IdentifyMatchHints Hints,
    IdentifyStructuralContext? StructuralContext = null,
    bool IncludeNsfw = false,
    bool IncludeRelationshipDetails = true,
    bool IncludeStructuralChildren = true);

/// <summary>
/// Request body for identifying one entity with a provider.
/// </summary>
/// <param name="Provider">Provider id to run the lookup against.</param>
/// <param name="Query">Optional user-provided query override.</param>
/// <param name="ParentExternalIds">
/// The just-resolved (but not yet applied) provider IDs of the entity's parent, supplied by the
/// client during incremental child identify. They are merged into the immediate ancestor snapshot
/// so a plugin can resolve a child within its parent's context (e.g. an album within an artist's
/// releases) even before the parent's metadata has been saved.
/// </param>
public sealed record IdentifyEntityRequest(
    string Provider,
    IdentifyQuery? Query,
    IReadOnlyDictionary<string, string>? ParentExternalIds = null);

/// <summary>
/// Request body for requesting identify searches for a batch of entities. Each entity gets its own
/// identify-search job and queue item; the dashboard tracks progress through queue item states.
/// </summary>
/// <param name="Provider">Provider to search with, or null to let the server walk enabled providers per entity.</param>
/// <param name="EntityIds">Entities to queue for identification.</param>
/// <param name="Query">Optional query override applied to every entity's search.</param>
public sealed record IdentifyBulkStartRequest(
    string? Provider,
    IReadOnlyList<Guid> EntityIds,
    IdentifyQuery? Query);

/// <summary>
/// Response for an accepted bulk identify request.
/// </summary>
/// <param name="Requested">Number of entities in the request.</param>
/// <param name="Enqueued">Number of identify-search jobs actually enqueued (duplicates pending searches are skipped).</param>
public sealed record IdentifyBulkAcceptedResponse(int Requested, int Enqueued);
