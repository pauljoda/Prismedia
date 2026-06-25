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

    public DateTimeOffset CreatedAt { get; set; }
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

    public bool Enabled { get; set; } = true;
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
/// Matching rules and import target for book acquisitions. The decision engine filters and ranks
/// release candidates against these rules; the import planner uses the target root and path template.
/// </summary>
public sealed class BookAcquisitionProfileRow {
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    /// <summary>Library root (must have book scanning enabled) the completed payload is imported into.</summary>
    public Guid TargetLibraryRootId { get; set; }

    /// <summary>Relative path template for the imported payload, with tokens like {Author}, {Title}, {Year}, {Volume}, {ext}.</summary>
    public string PathTemplate { get; set; } = "{Author}/{Title} ({Year})/{Title}{ - Volume}.{ext}";

    public ImportMode ImportMode { get; set; } = ImportMode.Move;

    /// <summary><see cref="BookFormat"/> codes acceptable for this profile. Empty allows all supported book formats.</summary>
    public string[] AllowedFormats { get; set; } = [];

    /// <summary>Required release language (free-form match against the indexer language). Null accepts any.</summary>
    public string? Language { get; set; }

    public int MinSeeders { get; set; } = 1;
    public long? MinSizeBytes { get; set; }
    public long? MaxSizeBytes { get; set; }
    public string[] RequiredTerms { get; set; } = [];
    public string[] IgnoredTerms { get; set; } = [];

    /// <summary>When true, automatically queue the best acceptable candidate instead of waiting for review.</summary>
    public bool AutoPick { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A single first-party acquisition: the intent to obtain one book, the metadata captured for the
/// identify handoff, and the state machine that walks search → download → import.
/// </summary>
public sealed class AcquisitionRow {
    public Guid Id { get; set; }

    /// <summary>Optional link to the originating request-history entry, kept loose so deleting either side is safe.</summary>
    public Guid? RequestHistoryId { get; set; }

    public Guid? ProfileId { get; set; }
    public AcquisitionStatus Status { get; set; } = AcquisitionStatus.Pending;
    public string? StatusMessage { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Series { get; set; }
    public int? Year { get; set; }
    public string? PosterUrl { get; set; }

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

    /// <summary>Set once the scan has applied the hint, so it is not re-applied on subsequent rescans.</summary>
    public bool Consumed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
