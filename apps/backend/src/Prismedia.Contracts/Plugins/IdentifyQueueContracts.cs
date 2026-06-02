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
/// Live progress for applying a reviewed Identify proposal.
/// </summary>
/// <param name="Id">Client-supplied operation identifier.</param>
/// <param name="EntityId">Root entity whose proposal is being applied.</param>
/// <param name="State">Progress state code: running, succeeded, or failed.</param>
/// <param name="CurrentIndex">One-based index for the entity currently being applied.</param>
/// <param name="Total">Estimated number of entity-level apply steps.</param>
/// <param name="CurrentKind">Entity kind currently being applied, when work has started.</param>
/// <param name="CurrentTitle">Entity title currently being applied, when work has started.</param>
/// <param name="CurrentPath">Structural title path from the root proposal to the current entity.</param>
/// <param name="Error">Failure message when the apply operation fails.</param>
/// <param name="UpdatedAt">When the progress snapshot last changed.</param>
public sealed record IdentifyApplyProgress(
    Guid Id,
    Guid EntityId,
    string State,
    int CurrentIndex,
    int Total,
    string? CurrentKind,
    string? CurrentTitle,
    IReadOnlyList<string> CurrentPath,
    string? Error,
    DateTimeOffset UpdatedAt);

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
/// <param name="ProgressId">Optional client-supplied operation id for polling live apply progress.</param>
public sealed record ApplyIdentifyQueueItemRequest(
    EntityMetadataProposal? Proposal,
    IReadOnlyList<string> SelectedFields,
    IReadOnlyDictionary<string, string?>? SelectedImages,
    Guid? ProgressId = null);

/// <summary>
/// Request body for persisting an in-progress identify proposal back onto a queued entity (without
/// applying it), so client-side progress such as incrementally resolved children survives refresh.
/// </summary>
/// <param name="Proposal">The queue item's own root proposal, updated with resolved children.</param>
public sealed record SaveIdentifyQueueProposalRequest(EntityMetadataProposal Proposal);
