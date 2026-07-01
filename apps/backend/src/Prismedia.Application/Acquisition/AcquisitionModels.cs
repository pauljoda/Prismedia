using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// A release returned by an indexer search, normalized across indexer families. This is the
/// Torznab/Prowlarr lingua franca reduced to the fields the decision engine and downloader need.
/// </summary>
public sealed record IndexerRelease(
    string Title,
    long SizeBytes,
    int? Seeders,
    int? Peers,
    DownloadProtocol Protocol,
    string? DownloadUrl,
    string? MagnetUrl,
    string? InfoHash,
    string? InfoUrl,
    string? Language,
    DateTimeOffset? PublishedAt);

/// <summary>
/// Decision rules a book acquisition profile contributes. Kept separate from the persistence row so
/// the decision engine is a pure function of (releases, rules) and trivially testable.
/// </summary>
public sealed record BookAcquisitionRules(
    IReadOnlyList<BookFormat> AllowedFormats,
    string? Language,
    int MinSeeders,
    long? MinSizeBytes,
    long? MaxSizeBytes,
    IReadOnlyList<string> RequiredTerms,
    IReadOnlyList<string> IgnoredTerms,
    IReadOnlyList<string> PreferredTerms,
    BookQualityRank MinQuality = default,
    BookQualityRank OwnedQuality = default,
    bool IsUpgradeSearch = false) {
    /// <summary>
    /// Permissive defaults used when no profile is configured yet (e.g. ad-hoc verification searches).
    /// <see cref="MinQuality"/> and <see cref="OwnedQuality"/> default to <see cref="BookQualityRank.Floor"/>
    /// (<c>default(BookQualityRank)</c>) and <see cref="IsUpgradeSearch"/> to false, so the quality and
    /// upgrade gates are no-ops unless explicitly set. <see cref="IsUpgradeSearch"/> — not the value of
    /// <see cref="OwnedQuality"/> — is the single source of truth for whether the upgrade gates apply, so a
    /// genuinely-unknown owned quality can never silently disable them.
    /// </summary>
    public static BookAcquisitionRules Default { get; } = new([], null, 1, null, null, [], [], []);
}

/// <summary>A release evaluated against the rules: its accept/reject verdict, ranking score, and any rejection reasons.</summary>
public sealed record ScoredRelease(
    IndexerRelease Release,
    Guid? IndexerConfigId,
    string IndexerName,
    bool Accepted,
    double Score,
    IReadOnlyList<ReleaseRejectionReason> Rejections);

/// <summary>Connection details an indexer search client needs to call an aggregator.</summary>
public sealed record IndexerConnection(
    Guid Id,
    IndexerKind Kind,
    string BaseUrl,
    string? ApiKey,
    IReadOnlyList<int> Categories);

/// <summary>A normalized search query handed to an indexer client.</summary>
public sealed record IndexerQuery(string Text, IReadOnlyList<int> Categories);

/// <summary>Result of probing an indexer connection.</summary>
public sealed record IndexerConnectionTest(bool Connected, string? Message);

/// <summary>An indexer that failed during a search; surfaced in the acquisition status so partial results stay transparent.</summary>
public sealed record IndexerSearchError(Guid IndexerId, string IndexerName, string Message);

/// <summary>Metadata captured when an acquisition is created, retained for the identify-hint handoff at import.</summary>
/// <param name="Kind">The media kind being acquired (book, movie, …); drives per-kind release scoring and import.</param>
/// <param name="EntityId">The wanted library entity this acquisition fulfils (request-created), or null for an ad-hoc acquisition.</param>
public sealed record AcquisitionMetadata(
    string Title,
    string? Author,
    string? Series,
    int? Year,
    string? PosterUrl,
    string? PluginId,
    string? PluginItemId,
    string? Description = null,
    EntityKind Kind = EntityKind.Book,
    Guid? EntityId = null);

/// <summary>The minimal input the background search job needs to query indexers for an acquisition.</summary>
public sealed record AcquisitionSearchInput(Guid Id, string Title, string? Author);

/// <summary>The outcome of running indexer searches for an acquisition: scored candidates plus any indexer failures.</summary>
public sealed record AcquisitionSearchOutcome(
    IReadOnlyList<ScoredRelease> Candidates,
    IReadOnlyList<IndexerSearchError> Errors);

/// <summary>Server-side view of a candidate selected for download, including the links kept out of the API surface.</summary>
public sealed record AcquisitionQueueCandidate(
    Guid CandidateId,
    string Title,
    string IndexerName,
    string? DownloadUrl,
    string? MagnetUrl,
    string? InfoHash,
    string? InfoUrl,
    DownloadProtocol Protocol);

/// <summary>
/// Snapshot of the release an acquisition was last sent to download, persisted so a later failure can
/// blocklist exactly that release. The <see cref="Identity"/> is computed the same way future search
/// candidates are, so the blocklist recognizes the release when it reappears.
/// </summary>
public sealed record SelectedRelease(string Title, string? IndexerName, string? InfoHash) {
    /// <summary>The normalized blocklist identity for this release. Derived, so it is not persisted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Identity => ReleaseIdentity.For(InfoHash, IndexerName, Title);
}

/// <summary>
/// Server-side reference to an accepted candidate the failed-handler may re-queue, carrying the fields
/// needed to compute its blocklist <see cref="Identity"/> and skip it when it is itself blocklisted.
/// </summary>
public sealed record AcquisitionCandidateRef(Guid CandidateId, string Title, string IndexerName, string? InfoHash) {
    /// <summary>The normalized blocklist identity for this candidate.</summary>
    public string Identity => ReleaseIdentity.For(InfoHash, IndexerName, Title);
}

/// <summary>A monitor whose periodic re-search is due now, paired with the acquisition the sweep should re-search.</summary>
/// <summary>A monitor whose periodic search is due. <see cref="IsUpgrade"/> marks an imported book due for an upgrade re-search (a child acquisition is spawned to search) rather than a still-missing book's initial search.</summary>
public sealed record DueMonitor(Guid MonitorId, Guid AcquisitionId, string Title, bool IsUpgrade = false);

/// <summary>
/// Everything the upgrade-replace job needs to swap a downloaded upgrade child's file in for the owned book:
/// the parent it upgrades, where the owned payload lives, the current owned quality to re-confirm against,
/// the new payload's download location, and the download-client item to clean up.
/// </summary>
public sealed record UpgradeReplaceTarget(
    Guid ParentId,
    string? ParentFinalSourcePath,
    Domain.Entities.BookQualityRank ParentOwnedQuality,
    string? ChildSelectedTitle,
    string? ChildContentPath,
    string? ChildClientItemId,
    Guid? ChildDownloadClientConfigId);

/// <summary>
/// Outcome of an in-place owned-file replacement. On success the owned file was atomically swapped for the
/// new one (the old kept beside it as a <c>.prismedia-bak</c>) and <see cref="NewFormat"/> is the installed
/// file's format; on failure <see cref="FailureReason"/> explains why and the owned file is untouched.
/// </summary>
public sealed record OwnedFileReplaceResult(bool Succeeded, string? SwappedPath, Domain.Entities.BookFormatTier NewFormat, string? FailureReason) {
    public static OwnedFileReplaceResult Failed(string reason) => new(false, null, Domain.Entities.BookFormatTier.Unknown, reason);
    public static OwnedFileReplaceResult Ok(string swappedPath, Domain.Entities.BookFormatTier newFormat) => new(true, swappedPath, newFormat, null);
}

/// <summary>
/// Replaces an owned book file in place with a strictly-better one. Finds the single importable book file
/// under the owned folder and under the new download path, verifies the new file and that both live on the
/// same filesystem (so the swap is an atomic rename), then renames the owned file aside to a
/// <c>.prismedia-bak</c> and moves the new file into its exact path. The backup is intentionally kept (it is
/// not an importable extension, so the scanner ignores it) so the previous file is always recoverable. On any
/// failure the owned file is left exactly as it was.
/// </summary>
public interface IOwnedFileReplacer {
    Task<OwnedFileReplaceResult> ReplaceAsync(string ownedFolder, string newContentPath, Domain.Entities.BookFormatTier ownedFormatTier, CancellationToken cancellationToken);
}

/// <summary>Input for adding a release identity to the acquisition blocklist (idempotent on the identity).</summary>
public sealed record BlocklistAddRequest(
    string Identity,
    BlocklistReason Reason,
    string? Title,
    string? IndexerName,
    string? InfoHash,
    Guid? AcquisitionId,
    string? Message);

/// <summary>Resolves an indexer release's info page into a usable magnet link when no direct link is provided.</summary>
public interface IReleaseLinkResolver {
    /// <summary>Fetches <paramref name="infoUrl"/> and extracts the first magnet link on the page, or null when none is found.</summary>
    Task<string?> ResolveMagnetAsync(string infoUrl, CancellationToken cancellationToken);
}

/// <summary>Minimal transfer wiring for an acquisition: its status, imported location, and download-client item.</summary>
public sealed record AcquisitionTransferInfo(
    AcquisitionStatus Status,
    string? FinalSourcePath,
    string? ClientItemId,
    Guid? DownloadClientConfigId);

/// <summary>Lists the files that landed on disk for an imported acquisition.</summary>
public interface IImportedFilesReader {
    /// <summary>Enumerates files under <paramref name="path"/> (recursively), or an empty list when the path is missing.</summary>
    IReadOnlyList<DownloadItemFile> List(string path);
}

/// <summary>An in-flight transfer the monitor advances, with the acquisition's current status for transition decisions.</summary>
public sealed record ActiveTransfer(
    Guid TransferId,
    Guid AcquisitionId,
    Guid? DownloadClientConfigId,
    string ClientItemId,
    AcquisitionStatus AcquisitionStatus,
    /// <summary>Last persisted transfer progress (0..1) from the previous monitor pass; compared against the live value to detect whether a stalled-looking torrent actually moved.</summary>
    double Progress,
    /// <summary>When the transfer was last touched by the monitor; used as the "last seen" anchor for the removal grace period.</summary>
    DateTimeOffset UpdatedAt,
    /// <summary>
    /// When the transfer was first observed stalled, or null if it is not currently stalled. The monitor
    /// anchors a stall here on first observation and abandons the download only once the stall persists past
    /// the grace window, so a briefly-unseeded torrent isn't blocklisted for a transient stall.
    /// </summary>
    DateTimeOffset? StalledSince = null);

/// <summary>Everything the import job needs: the captured metadata, the chosen profile, and the completed download's location.</summary>
public sealed record AcquisitionImportContext(
    Guid Id,
    string Title,
    string? Author,
    string? Series,
    int? Year,
    string? PosterUrl,
    string? PluginId,
    string? PluginItemId,
    Guid? ProfileId,
    string? ContentPath,
    string? ClientItemId,
    Guid? DownloadClientConfigId,
    string? Description = null);
