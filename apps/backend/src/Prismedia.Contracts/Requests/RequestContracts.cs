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
    bool Requestable);

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
