namespace Prismedia.Contracts.Plugins;

/// <summary>
/// Durable identify queue item returned by the API.
/// </summary>
/// <param name="Id">Stable queue row identifier.</param>
/// <param name="EntityId">Prismedia entity being identified.</param>
/// <param name="EntityKind">Entity kind code for provider filtering and UI grouping.</param>
/// <param name="Title">Current entity title.</param>
/// <param name="IsNsfw">Whether the queued entity is flagged as NSFW.</param>
/// <param name="State">Queue state code: search, proposal, done, deleted, or error.</param>
/// <param name="Provider">Provider code used by the latest search, when selected.</param>
/// <param name="Action">Provider action used by the latest search.</param>
/// <param name="Query">Latest user/provider query used for the search.</param>
/// <param name="Candidates">Search candidates waiting for user selection.</param>
/// <param name="Proposal">Hydrated metadata proposal waiting for review.</param>
/// <param name="Error">Latest identify or apply error, when present.</param>
/// <param name="CreatedAt">When the item was first queued.</param>
/// <param name="UpdatedAt">When queue state last changed.</param>
/// <param name="CompletedAt">When the item reached a terminal state.</param>
public sealed record IdentifyQueueItem(
    Guid Id,
    Guid EntityId,
    string EntityKind,
    string Title,
    bool IsNsfw,
    string State,
    string? Provider,
    string Action,
    IdentifyQuery? Query,
    IReadOnlyList<EntitySearchCandidate> Candidates,
    EntityMetadataProposal? Proposal,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>
/// Request body for starting or retrying an identify provider search.
/// </summary>
/// <param name="Provider">Provider code selected by the user.</param>
/// <param name="Query">Optional title, URL, or external ID override.</param>
public sealed record IdentifyQueueSearchRequest(string Provider, IdentifyQuery? Query);

/// <summary>
/// Request body for accepting a reviewed identify queue proposal.
/// </summary>
/// <param name="Proposal">Optional reviewed proposal payload. When null, the stored proposal is applied.</param>
/// <param name="SelectedFields">Root proposal field keys selected by the user.</param>
/// <param name="SelectedImages">Optional role-to-remote-URL artwork selections.</param>
public sealed record ApplyIdentifyQueueItemRequest(
    EntityMetadataProposal? Proposal,
    IReadOnlyList<string> SelectedFields,
    IReadOnlyDictionary<string, string?>? SelectedImages);
