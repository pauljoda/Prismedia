using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Requests;

/// <summary>Search query for requestable external media.</summary>
/// <param name="HideNsfw">When true, adults-only results (NC-17/X style certifications) are filtered out.</param>
public sealed record RequestSearchRequest(
    string Query,
    IReadOnlyList<RequestMediaKind> Kinds,
    IReadOnlyList<RequestProviderKind> Sources,
    bool HideNsfw);

/// <summary>Aggregated request search response.</summary>
public sealed record RequestSearchResponse(
    IReadOnlyList<RequestSearchResult> Results,
    IReadOnlyList<RequestProviderHealth> ProviderErrors);

/// <summary>Provider health warning captured while aggregating search results.</summary>
public sealed record RequestProviderHealth(Guid ServiceId, RequestProviderKind Kind, string DisplayName, string Message);

/// <summary>Normalized external search result.</summary>
/// <param name="Subtitle">Short secondary line for review context (e.g. the author for a book, work count for an author).</param>
/// <param name="Requestable">True when the item can be requested.</param>
/// <param name="ProviderName">Display name of the plugin/provider that sourced this result (e.g. "OpenLibrary"), for attribution in the results grid.</param>
/// <param name="PluginId">Stable manifest id of the plugin that produced this result.</param>
/// <param name="ExternalIdentity">Persistent identity selected from the plugin's declared namespaces.</param>
public sealed record RequestSearchResult(
    Guid ServiceId,
    RequestProviderKind Source,
    RequestMediaKind Kind,
    string ExternalId,
    string Title,
    string? Subtitle,
    int? Year,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    decimal? Rating,
    int? RuntimeMinutes,
    string? Certification,
    int? TrackCount,
    IReadOnlyList<string> Tags,
    bool Tracked,
    string? UpstreamId,
    bool? Monitored,
    bool Requestable,
    string? ProviderName = null,
    string? PluginId = null,
    ExternalIdentity? ExternalIdentity = null);

/// <summary>
/// Requests the canonical plugin proposal used to review one discovery result before committing it.
/// </summary>
/// <param name="Kind">Request-flow kind selected by the user.</param>
/// <param name="PluginId">Stable manifest id of the plugin that produced the search result.</param>
/// <param name="ExternalIdentity">Persistent upstream identity selected from the search result.</param>
public sealed record RequestReviewRequest(
    RequestMediaKind Kind,
    string PluginId,
    ExternalIdentity ExternalIdentity);

/// <summary>
/// Canonical, unflattened plugin proposal used by every request review surface.
/// </summary>
/// <param name="PluginId">Stable manifest id of the plugin that resolved the proposal.</param>
/// <param name="ExternalIdentity">Primary persistent identity used for the lookup.</param>
/// <param name="EntityKind">Actual entity kind targeted by the root proposal.</param>
/// <param name="Kind">Request-flow kind selected by the user.</param>
/// <param name="Proposal">Complete proposal, including nested structural children and relationships.</param>
/// <param name="Revision">Deterministic SHA-256 digest of the canonical proposal content.</param>
/// <param name="Targets">Requestable root and structural child targets represented by the proposal.</param>
public sealed record RequestReviewResponse(
    string PluginId,
    ExternalIdentity ExternalIdentity,
    EntityKind EntityKind,
    RequestMediaKind Kind,
    EntityMetadataProposal Proposal,
    string Revision,
    IReadOnlyList<RequestReviewTarget> Targets);

/// <summary>One independently identifiable root or structural child in a request review proposal.</summary>
/// <param name="ProposalId">Plugin-owned proposal id used by the review UI.</param>
/// <param name="Kind">Request-flow kind for this target.</param>
/// <param name="EntityKind">Actual Prismedia entity kind targeted by the proposal node.</param>
/// <param name="ExternalIdentity">Persistent identity declared by the selected plugin for this target kind.</param>
/// <param name="Requestable">Whether the request flow currently supports committing this target kind.</param>
/// <param name="Position">Provider-reported structural position, when available.</param>
/// <param name="Year">Provider-reported year, when available.</param>
/// <param name="Monitored">Monitoring state, when supplied by a future provider contract.</param>
public sealed record RequestReviewTarget(
    string ProposalId,
    RequestMediaKind Kind,
    EntityKind EntityKind,
    ExternalIdentity ExternalIdentity,
    bool Requestable,
    int? Position = null,
    int? Year = null,
    bool? Monitored = null);

/// <summary>Normalized external detail record for a requestable item.</summary>
/// <param name="Subtitle">Short secondary line for review context (e.g. the author for a book, the book count for an author).</param>
/// <param name="Dates">Provider date entries (code → ISO-ish value: release, firstAir, …) — the same dates identify would apply.</param>
/// <param name="Children">Selectable child works — an author's books, or a series' volumes — fanned out into one acquisition each.</param>
public sealed record RequestDetailResponse(
    RequestProviderKind Source,
    RequestMediaKind Kind,
    string ExternalId,
    string Title,
    string? Subtitle,
    int? Year,
    IReadOnlyDictionary<string, string> Dates,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    decimal? Rating,
    int? RuntimeMinutes,
    string? Certification,
    int? TrackCount,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Studios,
    IReadOnlyList<string> Credits,
    IReadOnlyList<RequestCastMember> Cast,
    IReadOnlyList<RequestRatingValue> Ratings,
    IReadOnlyList<RequestChildOption> Children,
    IReadOnlyList<RequestTrack> Tracks,
    bool Tracked,
    string? UpstreamId,
    bool? Monitored,
    RequestServiceOptionsResponse ServiceOptions);

/// <summary>One cast credit on a request detail, hydrated from the metadata provider.</summary>
/// <param name="Role">Character or role name when known.</param>
/// <param name="ImageUrl">Absolute profile/headshot URL when available.</param>
public sealed record RequestCastMember(string Name, string? Role, string? ImageUrl);

/// <summary>A rating from one source on a request detail.</summary>
/// <param name="Value">Score in the source's native scale.</param>
/// <param name="Scale">Maximum of the source's scale (10 for TMDB/IMDb, 100 for percent scores).</param>
/// <param name="Votes">Vote count when the source reports one.</param>
public sealed record RequestRatingValue(RequestRatingSource Source, decimal Value, decimal Scale, int? Votes);

/// <summary>Selectable or informational child option, such as a book in an author's bibliography or a series volume.</summary>
/// <param name="Number">Ordering number where the provider has one (volume number).</param>
/// <param name="Year">Release year when known.</param>
/// <param name="ItemCount">Child count for a container; null elsewhere.</param>
/// <param name="Monitored">Upstream monitored flag when the parent is tracked; null otherwise.</param>
public sealed record RequestChildOption(
    string Id,
    string Title,
    RequestMediaKind Kind,
    bool Requestable,
    int? Number,
    int? Year,
    int? ItemCount,
    string? Overview,
    string? PosterUrl,
    bool? Monitored);

/// <summary>One track on an album detail, for review before requesting.</summary>
public sealed record RequestTrack(int Number, string Title, int? DurationSeconds);

/// <summary>
/// Commits a request: creates the wanted library entity/entities for the reviewed item up front —
/// populated from the plugin proposal but with no file — and starts one acquisition per requested book.
/// </summary>
/// <param name="Kind">What the provider-qualified id addresses (a book, or an author container).</param>
/// <param name="ExternalId">Provider-qualified id (<c>"provider:itemId"</c>) of the reviewed item.</param>
/// <param name="SelectedChildIds">
/// Provider-qualified ids of the selected child works (an author's books, or a series' volumes). Required
/// for an author commit; empty for a standalone book, which requests itself.
/// </param>
/// <param name="TargetLibraryRootId">
/// The library root the acquired files should import into. Null uses the kind's default (the default
/// profile's target, else the first suitable root). An unsuitable root (wrong media flags) is ignored.
/// </param>
/// <param name="ProfileId">
/// The acquisition profile whose rules score this request's release searches. Null uses the kind's
/// default profile. A profile of the wrong kind is ignored.
/// </param>
/// <param name="Preset">
/// The monitoring preset for a container request (a series, an author, an artist). When
/// <see cref="SelectedChildIds"/> is non-empty the explicit selection wins and the preset is only recorded
/// on the container monitor (governing whether future syncs auto-monitor newly discovered works). When the
/// selection is empty the preset also derives which existing children to request now (see
/// <c>MonitorPresetSelection.Resolve</c>). Null defaults to <see cref="MonitorPreset.All"/>, matching the
/// pre-preset behavior. Ignored for leaf requests.
/// </param>
public sealed record RequestCommitRequest(
    RequestMediaKind Kind,
    string ExternalId,
    IReadOnlyList<string> SelectedChildIds,
    Guid? TargetLibraryRootId = null,
    Guid? ProfileId = null,
    MonitorPreset? Preset = null);

/// <summary>Per-item outcome of a request commit, linking the created wanted entity and its acquisition.</summary>
/// <param name="ExternalId">Provider-qualified id of the item this outcome describes.</param>
/// <param name="EntityId">The library entity (created wanted, or pre-existing) backing the item.</param>
/// <param name="AcquisitionId">The acquisition started for the item; null when none was started.</param>
public sealed record RequestCommitItem(
    string ExternalId,
    string Title,
    RequestCommitOutcome Outcome,
    Guid? EntityId,
    Guid? AcquisitionId);

/// <summary>Result of a request commit.</summary>
/// <param name="ContainerEntityId">The wanted container entity (the author) when the commit created/reused one.</param>
public sealed record RequestCommitResponse(
    Guid? ContainerEntityId,
    IReadOnlyList<RequestCommitItem> Items);

/// <summary>
/// Requests an existing library entity by id — a wanted placeholder's "Search for release". The server
/// resolves the entity's kind and provider identity itself, so callers never guess which of the
/// entity's external ids belongs to a plugin.
/// </summary>
/// <param name="TargetLibraryRootId">Optional import target override; null inherits the followed container's choice, else the kind default.</param>
/// <param name="ProfileId">Optional profile override; null inherits the followed container's choice, else the kind default.</param>
public sealed record RequestEntityCommitRequest(Guid EntityId, Guid? TargetLibraryRootId = null, Guid? ProfileId = null);

/// <summary>
/// Requests every still-wanted child phantom under an entity as its own monitored acquisition — a
/// season's missing episodes after a season pack imported with gaps.
/// </summary>
public sealed record MissingChildrenCommitRequest(Guid EntityId);

/// <summary>Outcome of a missing-children request: how many gaps are now covered by an acquisition, and how many exist.</summary>
public sealed record MissingChildrenCommitResponse(int Covered, int Missing);

/// <summary>
/// Removes wanted placeholders the user no longer wants: each is deleted (with any in-flight
/// acquisition torn down) and blacklisted from container discovery, so a followed author/artist sweep
/// never resurrects it. Explicitly requesting the same work again clears its blacklist entry.
/// </summary>
public sealed record WantedRemovalRequest(IReadOnlyList<Guid> EntityIds);

/// <summary>Result of a wanted removal.</summary>
/// <param name="Removed">How many placeholders were removed (on-disk items are skipped).</param>
public sealed record WantedRemovalResponse(int Removed);

/// <summary>Root folder/profile option exposed by a request service instance.</summary>
public sealed record RequestServiceOption(string Id, string Name, string? Path);

/// <summary>Grouped selectable options exposed by a request service instance.</summary>
public sealed record RequestServiceOptionsResponse(
    IReadOnlyList<RequestServiceOption> QualityProfiles,
    IReadOnlyList<RequestServiceOption> RootFolders,
    IReadOnlyList<RequestServiceOption> MetadataProfiles,
    IReadOnlyList<RequestServiceOption> Tags);
