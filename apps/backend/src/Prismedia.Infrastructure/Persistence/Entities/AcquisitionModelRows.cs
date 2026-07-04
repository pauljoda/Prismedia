using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>
/// Persisted configuration for an indexer aggregator (Prowlarr/Jackett) Prismedia searches for releases.
/// Secrets are kept in <see cref="IndexerCredentialRow"/> so config summaries never carry the API key.
/// </summary>
public sealed class IndexerConfigRow {
    public Guid Id { get; set; }
    public IndexerKind Kind { get; set; } = IndexerKind.Prowlarr;
    public string DisplayName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>Search preference order; lower numbers are tried/ranked ahead of higher ones.</summary>
    public int Priority { get; set; } = 25;

    /// <summary>Torznab category ids constraining the search (e.g. 7000 books, 8010 comics). Empty searches all.</summary>
    public int[] Categories { get; set; } = [];

    /// <summary>Optional hourly search-query budget for this indexer; null/0 means unlimited.</summary>
    public int? QueryLimitPerHour { get; set; }

    /// <summary>Seed-ratio goal for torrents grabbed from this indexer; null falls back to the client default.</summary>
    public double? SeedRatio { get; set; }

    /// <summary>Seed-time goal (minutes) for torrents grabbed from this indexer; null falls back to the client default.</summary>
    public int? SeedTimeMinutes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Health of one indexer: its failure-escalation level and the suppression window it is inside.
/// A row exists only after the indexer's first recorded failure; absence means healthy.
/// </summary>
public sealed class IndexerStatusRow {
    /// <summary>The indexer this status belongs to (primary key; cascades with the config).</summary>
    public Guid IndexerConfigId { get; set; }

    /// <summary>Current escalation level on the backoff ladder (0 = healthy).</summary>
    public int EscalationLevel { get; set; }

    /// <summary>End of the current suppression window; searches skip the indexer until then.</summary>
    public DateTimeOffset? DisabledUntil { get; set; }

    public string? LastFailureMessage { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Secret for an <see cref="IndexerConfigRow"/> (e.g. the Prowlarr API key). Stored verbatim, matching the existing request-credential pattern.</summary>
public sealed class IndexerCredentialRow {
    public Guid Id { get; set; }
    public Guid IndexerConfigId { get; set; }
    public string CredentialKey { get; set; } = string.Empty;
    public string EncryptedValue { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Persisted configuration for a download client (qBittorrent) Prismedia hands releases to.
/// The username lives on the row; the password is kept in <see cref="DownloadClientCredentialRow"/>.
/// </summary>
public sealed class DownloadClientConfigRow {
    public Guid Id { get; set; }
    public DownloadClientKind Kind { get; set; } = DownloadClientKind.QBittorrent;
    public string DisplayName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? Username { get; set; }

    /// <summary>Category/label applied to torrents Prismedia adds, so they can be filtered and managed in isolation.</summary>
    public string Category { get; set; } = "prismedia-books";

    /// <summary>Selection order among clients that speak the same protocol; lower is preferred (matches indexer priority convention).</summary>
    public int Priority { get; set; } = 25;

    /// <summary>Default seed-ratio goal for torrents in this client when the grab's indexer sets none.</summary>
    public double? SeedRatio { get; set; }

    /// <summary>Default seed-time goal (minutes) when the grab's indexer sets none.</summary>
    public int? SeedTimeMinutes { get; set; }

    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A path prefix rewrite for one download client whose filesystem view differs from Prismedia's
/// (split-host or container deployments): a client-reported path starting with <see cref="RemotePath"/>
/// is rewritten to <see cref="LocalPath"/> before Prismedia touches the files.
/// </summary>
public sealed class RemotePathMappingRow {
    public Guid Id { get; set; }
    public Guid DownloadClientConfigId { get; set; }
    public string RemotePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Secret for a <see cref="DownloadClientConfigRow"/> (e.g. the qBittorrent password). Stored verbatim.</summary>
public sealed class DownloadClientCredentialRow {
    public Guid Id { get; set; }
    public Guid DownloadClientConfigId { get; set; }
    public string CredentialKey { get; set; } = string.Empty;
    public string EncryptedValue { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A named, scored release classifier (a Sonarr-style custom format): a set of conditions over a release's
/// title/group/language/quality, scoped to one profile kind. The score a format is worth lives on the
/// profile (in <see cref="BookAcquisitionProfileRow.FormatScoresJson"/>), so the same format can be worth
/// different points in different profiles of its kind. Referenced by profiles of the same kind only.
/// </summary>
public sealed class CustomFormatRow {
    public Guid Id { get; set; }

    /// <summary>The profile kind this format applies to (book/movie/TV/music), matching the profile vocabulary.</summary>
    public EntityKind Kind { get; set; } = EntityKind.Book;

    /// <summary>User-facing name of the format (e.g. "English/Dual audio").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>JSON array of the format's conditions ([{"Type":"release-title","Value":"…","Negate":false,"Required":false}, …]).</summary>
    public string ConditionsJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Matching rules and import target for book acquisitions. The decision engine filters and ranks
/// release candidates against these rules; the import planner uses the target root and path template.
/// </summary>
public sealed class BookAcquisitionProfileRow {
    public Guid Id { get; set; }

    /// <summary>The media kind this profile's rules and import target apply to. IsDefault is per kind.</summary>
    public EntityKind Kind { get; set; } = EntityKind.Book;

    public string DisplayName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    /// <summary>Library root (must have book scanning enabled) the completed payload is imported into.</summary>
    public Guid TargetLibraryRootId { get; set; }

    /// <summary>Relative path template for the imported payload, with tokens like {Author}, {Title}, {Year}, {Volume}, {ext}.</summary>
    public string PathTemplate { get; set; } = "{Author}/{Title} ({Year})/{Title}{ - Volume}.{ext}";

    public ImportMode ImportMode { get; set; } = ImportMode.Move;

    /// <summary>
    /// Download-client category override for this profile's kind (e.g. <c>prismedia-movies</c>), so each
    /// kind's downloads stay segregated in the client. Null/blank uses the client's configured category.
    /// </summary>
    public string? DownloadCategory { get; set; }

    /// <summary><see cref="BookFormat"/> codes acceptable for this profile. Empty allows all supported book formats.</summary>
    public string[] AllowedFormats { get; set; } = [];

    /// <summary>
    /// Media quality codes (video or audio ladder, by the profile's kind) this profile accepts.
    /// Empty allows every quality — including unrecognized ones. Lets a profile pin exact tiers
    /// ("only bluray-1080p") or cap a ladder (exclude the 2160p rungs).
    /// </summary>
    public string[] AllowedQualities { get; set; } = [];

    /// <summary>
    /// Media quality code (video/audio ladder) at or above which the upgrade loop stops for this
    /// profile's kind. Null means any allowed quality fulfills immediately.
    /// </summary>
    public string? CutoffQuality { get; set; }

    /// <summary>
    /// Ordered preferred release languages (canonicalized case-insensitively against title tokens and the
    /// indexer language attribute). A release declaring only other languages is rejected; earlier entries
    /// rank higher. Empty disables the language gate.
    /// </summary>
    public string[] PreferredLanguages { get; set; } = ["English"];

    public int MinSeeders { get; set; } = 1;
    public long? MinSizeBytes { get; set; }
    public long? MaxSizeBytes { get; set; }
    public string[] RequiredTerms { get; set; } = [];
    public string[] IgnoredTerms { get; set; } = [];

    /// <summary>Terms that boost a release's ranking when present in its title (e.g. "retail", "epub"); each match outranks seeders.</summary>
    public string[] PreferredTerms { get; set; } = [];

    /// <summary>JSON array of custom weighted terms ([{"Term":"remux","Weight":100}, …]) added to a matching release's ranking score.</summary>
    public string WeightedTermsJson { get; set; } = "[]";

    /// <summary>When true, automatically queue the best acceptable candidate instead of waiting for review.</summary>
    public bool AutoPick { get; set; }

    /// <summary>
    /// When true, a failed download is automatically blocklisted and the next-best acceptable candidate is
    /// grabbed without manual intervention. When false, a failed download blocklists the release but leaves
    /// the acquisition <see cref="AcquisitionStatus.Failed"/> for the user to retry.
    /// </summary>
    public bool AutoRedownload { get; set; }

    /// <summary>When true, an imported book is kept under watch and re-searched for a higher-quality release until the cutoff is reached.</summary>
    public bool UpgradeUntilCutoff { get; set; }

    /// <summary>The source tier at or above which the upgrade loop stops searching. Half of the cutoff quality.</summary>
    public BookSourceTier CutoffSourceTier { get; set; } = BookSourceTier.Unknown;

    /// <summary>The format tier at or above which the upgrade loop stops searching. Half of the cutoff quality.</summary>
    public BookFormatTier CutoffFormatTier { get; set; } = BookFormatTier.Unknown;

    /// <summary>
    /// JSON map of custom-format id → score ({"&lt;guid&gt;":500, …}) for the formats of this profile's kind.
    /// A matching format adds its score to a release's ranking; a format absent from the map (or scored 0) is
    /// ignored. Scores are clamped to ±10_000 like weighted terms.
    /// </summary>
    public string FormatScoresJson { get; set; } = "{}";

    /// <summary>The minimum total custom-format score a release must clear to be accepted (Sonarr's min format score). 0 by default.</summary>
    public int MinFormatScore { get; set; }

    /// <summary>The custom-format score at or above which the upgrade loop stops chasing better-scoring releases. Null = no format-score cutoff.</summary>
    public int? CutoffFormatScore { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A single first-party acquisition: the intent to obtain one book, the metadata captured for the
/// identify handoff, and the state machine that walks search → download → import.
/// </summary>
public sealed class AcquisitionRow {
    public Guid Id { get; set; }

    /// <summary>Media kind being acquired; drives per-kind release scoring, import, and monitoring.</summary>
    public EntityKind Kind { get; set; } = EntityKind.Book;

    /// <summary>
    /// The wanted library entity this acquisition fulfils, created up front by a request commit. The
    /// import attaches the downloaded file to exactly this entity (no duplicate is scanned in) and
    /// clears its Wanted state. Null for ad-hoc acquisitions with no pre-created entity. Loose link
    /// (no FK): the entity graph is a different bounded slice, so deleting the entity must not touch
    /// the acquisition record — consumers tolerate a dangling id and fall back to the scan-created path.
    /// </summary>
    public Guid? EntityId { get; set; }

    public Guid? ProfileId { get; set; }

    /// <summary>
    /// The request-time library-root choice this acquisition should import into. Null uses the kind's
    /// default. Loose link (no FK) — the import validates and degrades to the default when it dangles.
    /// </summary>
    public Guid? TargetLibraryRootId { get; set; }

    public AcquisitionStatus Status { get; set; } = AcquisitionStatus.Pending;
    public string? StatusMessage { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Series { get; set; }

    /// <summary>Season number for TV units (a season-pack or single-episode acquisition). Null elsewhere.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Episode number for a single-episode acquisition. Null elsewhere (a season pack has only a season).</summary>
    public int? EpisodeNumber { get; set; }

    public int? Year { get; set; }
    public string? PosterUrl { get; set; }

    /// <summary>Description/overview captured at request time, held so the acquisition surface is populated before import and used to seed the imported book.</summary>
    public string? Description { get; set; }

    /// <summary>Plugin manifest id that supplied the request metadata, used to stamp the imported entity for ID-first identify.</summary>
    public string? PluginId { get; set; }

    /// <summary>The plugin's item id for this book (the external-id value paired with <see cref="PluginId"/>).</summary>
    public string? PluginItemId { get; set; }

    /// <summary>Provider → external id map captured at request time (jsonb).</summary>
    public string ExternalIdsJson { get; set; } = "{}";

    /// <summary>Source URLs captured at request time (jsonb array).</summary>
    public string SourceUrlsJson { get; set; } = "[]";

    /// <summary>Snapshot of the release the user selected to download (jsonb).</summary>
    public string? SelectedReleaseJson { get; set; }

    /// <summary>Final on-disk path of the imported payload, used as the identify-hint key.</summary>
    public string? FinalSourcePath { get; set; }

    /// <summary>Detected source tier of the release this acquisition imported (the owned quality, source axis).</summary>
    public BookSourceTier OwnedSourceTier { get; set; } = BookSourceTier.Unknown;

    /// <summary>Detected format tier of the release this acquisition imported (the owned quality, format axis).</summary>
    public BookFormatTier OwnedFormatTier { get; set; } = BookFormatTier.Unknown;

    /// <summary>
    /// Detected media quality of the release this acquisition imported (the owned quality on the kind's
    /// video/audio ladder — <see cref="VideoQuality"/> for movies and TV, <see cref="AudioQuality"/> for
    /// music), captured from the selected release title. Null for book kinds (they use the source/format
    /// tiers above) and for imports that recorded no selected release.
    /// </summary>
    public string? OwnedMediaQuality { get; set; }

    /// <summary>
    /// Detected revision of the release this acquisition imported (the PROPER/REPACK/RERIP and anime-version
    /// axis, parsed from the selected release title). Pairs with <see cref="OwnedMediaQuality"/>: under the
    /// prefer-and-upgrade proper policy a same-quality release with a strictly higher revision than this
    /// counts as an upgrade. Defaults to 1 (a plain release); books ignore it.
    /// </summary>
    public int OwnedMediaRevision { get; set; } = 1;

    /// <summary>
    /// Total custom-format score of the release this acquisition imported (computed at import from the
    /// selected release title against the profile's custom formats). Advanced by the upgrade replace handler
    /// alongside the quality/revision. Feeds the upgrade loop's same-quality format-score cutoff. Defaults to 0.
    /// </summary>
    public int OwnedFormatScore { get; set; }

    /// <summary>
    /// For an upgrade child acquisition, the parent acquisition it replaces. Self-FK, nulled if the parent is
    /// hard-deleted. Null for an ordinary (non-upgrade) acquisition.
    /// </summary>
    public Guid? UpgradeOfAcquisitionId { get; set; }

    /// <summary>
    /// Set in the same commit that transitions to <see cref="AcquisitionStatus.Imported"/> and records the
    /// owned tiers, so the upgrade due-policy treats the owned quality as authoritative only once captured
    /// (a not-yet-captured import is never mistaken for "owns nothing").
    /// </summary>
    public bool UpgradeQualityCaptured { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A scored release returned by an indexer search for an acquisition. Rejected candidates are retained with reasons for transparency.</summary>
public sealed class ReleaseCandidateRow {
    public Guid Id { get; set; }
    public Guid AcquisitionId { get; set; }
    public Guid? IndexerConfigId { get; set; }
    public string IndexerName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int? Seeders { get; set; }
    public int? Peers { get; set; }
    public DownloadProtocol Protocol { get; set; } = DownloadProtocol.Torrent;
    public string? DownloadUrl { get; set; }
    public string? MagnetUrl { get; set; }
    public string? InfoHash { get; set; }
    public string? InfoUrl { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Engine ranking score; higher is preferred among accepted candidates.</summary>
    public double Score { get; set; }

    public bool Accepted { get; set; }

    /// <summary><see cref="ReleaseRejectionReason"/> codes explaining why the candidate was rejected (jsonb array).</summary>
    public string RejectionsJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Links an acquisition to its in-flight item in a download client, tracking transfer progress.</summary>
public sealed class DownloadTransferRow {
    public Guid Id { get; set; }
    public Guid AcquisitionId { get; set; }
    public Guid? DownloadClientConfigId { get; set; }

    /// <summary>Identifier of the item in the download client (the torrent info hash for qBittorrent).</summary>
    public string ClientItemId { get; set; } = string.Empty;

    public string? Category { get; set; }
    public string? SavePath { get; set; }
    public string? ContentPath { get; set; }

    /// <summary>Transfer progress in the range 0..1.</summary>
    public double Progress { get; set; }

    /// <summary>Raw client state string, surfaced for display.</summary>
    public string? State { get; set; }

    /// <summary>When the transfer was first observed stalled, or null when not currently stalled.</summary>
    public DateTimeOffset? StalledSince { get; set; }

    /// <summary>Seed-ratio goal captured at grab time (indexer's, else client default); null means no ratio goal.</summary>
    public double? SeedGoalRatio { get; set; }

    /// <summary>Seed-time goal in minutes captured at grab time; null means no time goal.</summary>
    public int? SeedGoalTimeMinutes { get; set; }

    /// <summary>
    /// Set when a hardlink/copy import handed the torrent to the seeding watch: the monitor keeps
    /// polling it and removes it (with data) once a seed goal is met. Null when not under watch.
    /// </summary>
    public DateTimeOffset? SeedingSince { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Path-keyed identity hint written just before a completed acquisition enqueues a book scan. The
/// scan stamps these external/plugin ids onto the newly created book entity so the existing
/// auto-identify pipeline resolves it ID-first instead of re-discovering what the request already knew.
/// </summary>
public sealed class AcquisitionImportHintRow {
    public Guid Id { get; set; }
    public Guid AcquisitionId { get; set; }

    /// <summary>
    /// The wanted library entity the import should attach to (copied from the acquisition at hint-write
    /// time). The book scan binds the imported path to this entity before its path-keyed upsert, so the
    /// wanted entity becomes the scanned entity instead of a duplicate. Null for pre-wanted acquisitions.
    /// </summary>
    public Guid? EntityId { get; set; }

    /// <summary>Normalized absolute path of the imported payload; the lookup key the scan matches against.</summary>
    public string SourcePath { get; set; } = string.Empty;

    public string? PluginId { get; set; }
    public string? PluginItemId { get; set; }
    public string ExternalIdsJson { get; set; } = "{}";
    public string SourceUrlsJson { get; set; } = "[]";
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Series { get; set; }
    public int? Year { get; set; }
    public string? PosterUrl { get; set; }

    /// <summary>Request-time description carried to the scan so the imported book is seeded with it (only when it has none yet).</summary>
    public string? Description { get; set; }

    /// <summary>Owned source tier captured at import, applied to the book's detail row by the scan hint.</summary>
    public BookSourceTier OwnedSourceTier { get; set; } = BookSourceTier.Unknown;

    /// <summary>Owned format tier captured at import (derived from the placed file), applied by the scan hint.</summary>
    public BookFormatTier OwnedFormatTier { get; set; } = BookFormatTier.Unknown;

    /// <summary>Set once the scan has applied the hint, so it is not re-applied on subsequent rescans.</summary>
    public bool Consumed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A provider work identity the user explicitly removed from Wanted — the discovery blacklist.
/// Container sweeps (a followed author/artist) skip suppressed works so a removed phantom never
/// reappears; explicitly requesting the same work again clears its suppression. One row per provider
/// identity the removed entity carried, so a sync through any of its providers stays suppressed.
/// </summary>
public sealed class WantedSuppressionRow {
    public Guid Id { get; set; }

    /// <summary>Provider code half of the suppressed identity (e.g. a plugin id, or isbn13).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The provider's item id for the suppressed work.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Media kind of the removed entity, for display.</summary>
    public EntityKind Kind { get; set; } = EntityKind.Book;

    /// <summary>Title at removal time, for a future management surface.</summary>
    public string Title { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A release identity refused for future acquisition. Populated when a download fails (auto-recovery)
/// or when the user manually blocks a release, and consulted by the decision engine so the same bad
/// release is never re-grabbed. The identity is computed by <c>ReleaseIdentity</c> (info hash first,
/// else normalized indexer + title); the display fields are retained for the management surface.
/// </summary>
public sealed class AcquisitionBlocklistRow {
    public Guid Id { get; set; }

    /// <summary>Normalized release identity (the lookup key); unique across the blocklist.</summary>
    public string Identity { get; set; } = string.Empty;

    public BlocklistReason Reason { get; set; } = BlocklistReason.Failed;

    /// <summary>Release title at the time it was blocklisted, for display.</summary>
    public string? Title { get; set; }

    /// <summary>Indexer the blocklisted release came from, for display.</summary>
    public string? IndexerName { get; set; }

    /// <summary>Torrent info hash when known, for display/debugging.</summary>
    public string? InfoHash { get; set; }

    /// <summary>Acquisition that triggered the block, kept loose so deleting it leaves the blocklist intact.</summary>
    public Guid? AcquisitionId { get; set; }

    /// <summary>Optional human-readable detail (e.g. the failure message).</summary>
    public string? Message { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One durable entry in the acquisition activity log — a grabbed/imported/failed/removed event for an
/// acquisition. The log OUTLIVES the acquisition it describes: <see cref="AcquisitionId"/> is a loose FK
/// with SetNull delete behavior, so hard-deleting an acquisition (cancel/delete) leaves its history intact
/// with a null acquisition id. The row denormalizes the acquisition's title/kind/entity and the release
/// context available at the moment of the event, so the log reads without re-joining a possibly-gone
/// acquisition. Append-only: rows are only ever inserted.
/// </summary>
public sealed class AcquisitionHistoryRow {
    public Guid Id { get; set; }

    /// <summary>
    /// The acquisition this event belongs to. Loose FK (SetNull): survives the acquisition's hard delete
    /// as a null so the audit trail is permanent. Null once the acquisition is gone, or for an event
    /// recorded without a resolvable acquisition.
    /// </summary>
    public Guid? AcquisitionId { get; set; }

    /// <summary>The library entity the acquisition targeted, denormalized so history can be filtered per entity even after the acquisition is deleted. Loose link (no FK).</summary>
    public Guid? EntityId { get; set; }

    /// <summary>Media kind the event concerns, for the log's kind badge.</summary>
    public EntityKind Kind { get; set; } = EntityKind.Book;

    /// <summary>Which lifecycle event this row records.</summary>
    public AcquisitionHistoryEvent Event { get; set; } = AcquisitionHistoryEvent.Grabbed;

    /// <summary>The acquisition's display title at the time of the event.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The release title the event concerned (the grabbed/imported/failed release), when known.</summary>
    public string? ReleaseTitle { get; set; }

    /// <summary>Indexer the release came from, when known.</summary>
    public string? IndexerName { get; set; }

    /// <summary>Download client the release was handed to, when known.</summary>
    public string? DownloadClientName { get; set; }

    /// <summary>Detected quality code (a video/audio ladder code, or a book source/format rank) at the event, when known.</summary>
    public string? QualityCode { get; set; }

    /// <summary>Total custom-format score of the release at the event, when computed.</summary>
    public int? FormatScore { get; set; }

    /// <summary>Human-readable detail (a failure reason, an old→new upgrade summary), when relevant.</summary>
    public string? Message { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A standing intent that Prismedia keeps acting on — the persistent counterpart to a one-and-done
/// acquisition. In this version a monitor keeps a wanted book's acquisition alive: while
/// <see cref="MonitorStatus.Active"/> the scheduler periodically re-runs its release search until the
/// book is acquired. The <see cref="Kind"/> discriminator is present from day one so later monitor kinds
/// (authors, series) share this table; only <see cref="EntityKind.Book"/> monitors are honored for now.
/// </summary>
public sealed class MonitorRow {
    public Guid Id { get; set; }

    /// <summary>The kind of target this monitor watches. Books only in this version.</summary>
    public EntityKind Kind { get; set; } = EntityKind.Book;

    /// <summary>The acquisition this monitor keeps re-searching. Nulled (and the monitor auto-paused) if that acquisition is hard-deleted.</summary>
    public Guid? AcquisitionId { get; set; }

    /// <summary>
    /// The library container entity (an author, an artist) this monitor watches for NEW works, for
    /// monitors not tied to a single acquisition. The sweep re-resolves the container from its provider
    /// ids and requests missing works as wanted placeholders. Loose link (no FK) into the entity graph;
    /// a dangling id auto-pauses the monitor at the next sweep.
    /// </summary>
    public Guid? EntityId { get; set; }

    /// <summary>Request-time library-root choice stored by an explicit container request; phantom requests inherit it. Loose link.</summary>
    public Guid? TargetLibraryRootId { get; set; }

    /// <summary>Request-time profile choice stored by an explicit container request; phantom requests inherit it. Loose link.</summary>
    public Guid? ProfileId { get; set; }

    /// <summary>
    /// The monitoring preset chosen when the container was requested. Only consulted for container
    /// monitors (an author/artist/series watch, <see cref="EntityId"/> set): it governs whether the
    /// discovery sync auto-monitors newly-discovered works — <see cref="MonitorPreset.All"/> and
    /// <see cref="MonitorPreset.Future"/> do; every other preset keeps monitoring the works committed up
    /// front but ignores new arrivals. Per-item monitors ignore this. Defaults to
    /// <see cref="MonitorPreset.All"/> so a container created without an explicit preset (an older row, a
    /// bare monitor toggle) keeps mirroring the container.
    /// </summary>
    public MonitorPreset Preset { get; set; } = MonitorPreset.All;

    public MonitorStatus Status { get; set; } = MonitorStatus.Active;

    /// <summary>Denormalized title of the wanted item, for the monitored list and job labels.</summary>
    public string Title { get; set; } = string.Empty;

    public string? Author { get; set; }

    /// <summary>When the monitor was last re-searched; null means never. Used to decide whether it is due.</summary>
    public DateTimeOffset? LastSearchedAt { get; set; }

    /// <summary>
    /// The imported book entity this monitor watches for upgrades, set once the acquisition imports. Lets the
    /// monitor outlive the (transient) acquisition and re-search for a better release of the owned book.
    /// </summary>
    public Guid? BookEntityId { get; set; }

    /// <summary>Number of successful upgrade replacements so far, for the replacement cap (reset when the cutoff is met).</summary>
    public int UpgradeAttempts { get; set; }

    /// <summary>Consecutive upgrade searches that found nothing strictly better, for the barren-search cap and backoff.</summary>
    public int BarrenSearches { get; set; }

    /// <summary>The in-flight upgrade child acquisition, if one is currently downloading. The interlock that allows at most one upgrade attempt per book at a time.</summary>
    public Guid? UpgradeChildAcquisitionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
