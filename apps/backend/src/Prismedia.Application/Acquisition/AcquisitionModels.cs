using Prismedia.Contracts.Acquisition;
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
    IReadOnlyList<string> PreferredLanguages,
    int MinSeeders,
    long? MinSizeBytes,
    long? MaxSizeBytes,
    IReadOnlyList<string> RequiredTerms,
    IReadOnlyList<string> IgnoredTerms,
    IReadOnlyList<string> PreferredTerms,
    IReadOnlyList<WeightedTerm> WeightedTerms,
    BookQualityRank MinQuality = default,
    BookQualityRank OwnedQuality = default,
    bool IsUpgradeSearch = false,
    // TV unit context, set per search by the runner (like the upgrade fields), never by a profile:
    // the season the unit belongs to, and the episode number when the unit is a single episode.
    int? SeasonNumber = null,
    int? EpisodeNumber = null) {
    /// <summary>
    /// The transfer protocols an enabled download client can acquire, set per search by the runner from
    /// the configured clients (never by a profile). Defaults to torrent-only so rules built without a
    /// client lookup keep the historical behavior.
    /// </summary>
    public IReadOnlyList<DownloadProtocol> AllowedProtocols { get; init; } = [DownloadProtocol.Torrent];

    /// <summary>
    /// Media quality codes (the kind's video/audio ladder) the profile accepts; empty allows all.
    /// Book kinds ignore this (they gate on <see cref="AllowedFormats"/> + <see cref="MinQuality"/>).
    /// </summary>
    public IReadOnlyList<string> AllowedQualities { get; init; } = [];

    /// <summary>Media quality code at/above which upgrades stop; null = fulfill at any allowed quality.</summary>
    public string? CutoffQuality { get; init; }

    /// <summary>Owned media quality code for an upgrade search (set per search by the runner, like the book owned rank).</summary>
    public string? OwnedMediaQuality { get; init; }

    /// <summary>
    /// Owned media revision for an upgrade search (set per search by the runner from the parent's stored
    /// revision, like <see cref="OwnedMediaQuality"/>). Under <see cref="ProperDownloadPolicy.PreferAndUpgrade"/>
    /// a same-quality candidate with a strictly higher revision than this counts as an upgrade. Defaults to 1.
    /// </summary>
    public int OwnedMediaRevision { get; init; } = 1;

    /// <summary>
    /// How PROPER/REPACK/RERIP and anime v2+ revisions affect ranking and upgrades. Set per search by the
    /// runner from the app setting, never by a profile (like the protocol and TV-unit facts). Defaults to
    /// <see cref="ProperDownloadPolicy.PreferAndUpgrade"/> so rules built without the settings reader keep
    /// the preferring behavior.
    /// </summary>
    public ProperDownloadPolicy ProperPolicy { get; init; } = ProperDownloadPolicy.PreferAndUpgrade;

    /// <summary>
    /// The profile kind these rules were resolved for, so <see cref="CustomFormatEvaluation"/> places a
    /// release title on the correct quality ladder when a format carries a quality condition. Defaults to
    /// <see cref="EntityKind.Book"/> so rules built without a profile (the permissive default) keep the
    /// established book behavior.
    /// </summary>
    public EntityKind Kind { get; init; } = EntityKind.Book;

    /// <summary>
    /// The profile's scored custom formats (Sonarr-style named release classifiers). Each matching format
    /// adds its score to a release's preference points (see <see cref="MediaReleaseEvaluation.PreferenceScore"/>).
    /// Resolved by the profile store against the custom-format table; formats scored 0 are skipped. Empty
    /// disables custom-format scoring and the min-format-score gate.
    /// </summary>
    public IReadOnlyList<ScoredCustomFormat> CustomFormats { get; init; } = [];

    /// <summary>
    /// The floor a release's total custom-format score must clear to be accepted (Sonarr's minimum custom
    /// format score). Enforced by <see cref="MinFormatScoreSpecification"/> only when
    /// <see cref="CustomFormats"/> is non-empty. Default 0 accepts any non-negative-scoring release.
    /// </summary>
    public int MinFormatScore { get; init; }

    /// <summary>
    /// The custom-format score at or above which the upgrade loop stops chasing better-scoring releases at
    /// the same ladder position (parallel to <see cref="CutoffQuality"/> on the format axis). Null means
    /// format score never keeps an item due for upgrade.
    /// </summary>
    public int? CutoffFormatScore { get; init; }

    /// <summary>
    /// The owned copy's custom-format score for an upgrade search (set per search by the runner from the
    /// parent's stored score, like <see cref="OwnedMediaQuality"/>). A same-ladder-position, same-revision
    /// candidate whose format score is strictly higher counts as an upgrade only while this is below
    /// <see cref="CutoffFormatScore"/>. Defaults to 0.
    /// </summary>
    public int OwnedFormatScore { get; init; }

    /// <summary>
    /// Permissive defaults used when no profile is configured yet (e.g. ad-hoc verification searches).
    /// <see cref="MinQuality"/> and <see cref="OwnedQuality"/> default to <see cref="BookQualityRank.Floor"/>
    /// (<c>default(BookQualityRank)</c>) and <see cref="IsUpgradeSearch"/> to false, so the quality and
    /// upgrade gates are no-ops unless explicitly set. <see cref="IsUpgradeSearch"/> — not the value of
    /// <see cref="OwnedQuality"/> — is the single source of truth for whether the upgrade gates apply, so a
    /// genuinely-unknown owned quality can never silently disable them.
    /// </summary>
    public static BookAcquisitionRules Default { get; } = new([], [], 1, null, null, [], [], [], []);
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
/// <param name="ProfileId">The request-time profile choice whose rules govern this acquisition; null uses the kind's default.</param>
/// <param name="TargetLibraryRootId">The request-time import-target choice; null uses the kind's default.</param>
/// <param name="SeasonNumber">Season number for TV units (season pack or single episode); null elsewhere.</param>
/// <param name="EpisodeNumber">Episode number for a single-episode acquisition; null elsewhere.</param>
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
    Guid? EntityId = null,
    Guid? ProfileId = null,
    Guid? TargetLibraryRootId = null,
    int? SeasonNumber = null,
    int? EpisodeNumber = null);

/// <summary>The minimal input the background search job needs to query indexers for an acquisition.</summary>
/// <param name="Kind">The media kind being acquired; picks the decision engine and the Torznab category range.</param>
/// <param name="EntityId">The wanted library entity this acquisition fulfils; a wanted-linked search auto-grabs its best accepted release.</param>
/// <param name="Year">Release year context for the query ladder (year-disambiguated movie searches).</param>
/// <param name="ProfileId">The acquisition's chosen profile; its rules score the search (null = the kind's default).</param>
/// <param name="Series">Series context for TV units — the query ladder leads every rung with it.</param>
/// <param name="SeasonNumber">Season number for TV units; builds the S01 / Season 1 rungs.</param>
/// <param name="EpisodeNumber">Episode number for a single-episode acquisition; builds the S01E05 rungs.</param>
public sealed record AcquisitionSearchInput(
    Guid Id, string Title, string? Author, EntityKind Kind = EntityKind.Book, Guid? EntityId = null,
    int? Year = null, Guid? ProfileId = null, string? Series = null, int? SeasonNumber = null, int? EpisodeNumber = null);

/// <summary>
/// Request-time acquisition choices (import target, profile) threaded from a commit to the acquisitions
/// and container monitors it creates, so later sweep- and phantom-created work inherits them.
/// </summary>
public sealed record AcquisitionTargeting(Guid? TargetLibraryRootId, Guid? ProfileId) {
    public static AcquisitionTargeting None { get; } = new(null, null);
    public bool IsEmpty => TargetLibraryRootId is null && ProfileId is null;
}

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
    DownloadProtocol Protocol,
    Guid? IndexerConfigId = null);

/// <summary>
/// The seed goal captured at grab time: the grab indexer's ratio/time settings, falling back to the
/// download client's defaults. A torrent imported by hardlink/copy keeps seeding until EITHER goal is
/// met (Sonarr semantics); a goal-less transfer is left to the client's own rules.
/// </summary>
public sealed record TransferSeedGoal(double? Ratio, int? TimeMinutes) {
    public bool IsEmpty => Ratio is null && TimeMinutes is null;
}

/// <summary>A transfer under seeding watch, with the goal it must meet before removal.</summary>
public sealed record SeedingTransfer(
    Guid TransferId,
    Guid AcquisitionId,
    Guid? DownloadClientConfigId,
    string ClientItemId,
    double? GoalRatio,
    int? GoalTimeMinutes,
    DateTimeOffset SeedingSince);

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

/// <summary>
/// A monitor whose periodic action is due. <see cref="IsUpgrade"/> marks an imported book due for an
/// upgrade re-search (a child acquisition is spawned to search). <see cref="EntityId"/> marks a
/// container monitor due for a discovery sync (re-resolve the author/artist from its provider and
/// request missing works); container dues carry no acquisition.
/// </summary>
public sealed record DueMonitor(Guid MonitorId, Guid? AcquisitionId, string Title, bool IsUpgrade = false, Guid? EntityId = null);

/// <summary>
/// The owned quality an upgrade child must beat, expressed in the vocabulary of the child's kind. A book
/// child carries <see cref="BookRank"/> (the parent's source/format tiers); a movie or single-episode
/// child carries <see cref="MediaQualityCode"/> (the parent's position on the video ladder). Exactly one
/// axis is populated per kind; the search runner selects the matching gate. A null record (not this type)
/// means the acquisition is not an upgrade child at all.
/// </summary>
/// <param name="BookRank">The parent's owned book quality for a book upgrade child; default for a media child.</param>
/// <param name="MediaQualityCode">The parent's owned ladder code for a media upgrade child; null for a book child.</param>
/// <param name="MediaRevision">
/// The parent's owned revision for a media upgrade child (PROPER/REPACK axis), so a same-quality
/// higher-revision candidate can be recognized as an upgrade. Defaults to 1 (a plain release); ignored on
/// the book axis, which has no revision concept.
/// </param>
/// <param name="FormatScore">
/// The parent's owned custom-format score, so a same-quality candidate with a strictly higher format score
/// (below the profile's cutoff) can be recognized as an upgrade. Defaults to 0.
/// </param>
public sealed record UpgradeOwnedQuality(Domain.Entities.BookQualityRank? BookRank, string? MediaQualityCode, int MediaRevision = 1, int FormatScore = 0);

/// <summary>
/// Everything the upgrade-replace job needs to swap a downloaded upgrade child's file in for the owned copy:
/// the parent it upgrades, its kind (which owned-quality vocabulary and file finder to use), where the owned
/// payload lives, the current owned quality to re-confirm against, the new payload's download location, and
/// the download-client item to clean up.
/// </summary>
/// <param name="ParentKind">The parent acquisition's media kind; routes the handler between the book and media replace paths.</param>
/// <param name="ParentOwnedMediaQuality">The parent's owned ladder code for a media parent; null for a book parent.</param>
/// <param name="ParentOwnedMediaRevision">The parent's owned revision for a media parent, so a same-quality higher-revision child is recognized as an upgrade at the pre-swap re-confirm gate. Defaults to 1; ignored on the book path.</param>
/// <param name="ParentProfileId">The parent's chosen profile, so the handler can re-score the child release against the same custom formats. Null uses the kind's default profile.</param>
/// <param name="ParentOwnedFormatScore">The parent's owned custom-format score, re-confirmed against at the pre-swap gate for a same-quality format-score upgrade. Defaults to 0.</param>
public sealed record UpgradeReplaceTarget(
    Guid ParentId,
    string? ParentFinalSourcePath,
    Domain.Entities.BookQualityRank ParentOwnedQuality,
    string? ChildSelectedTitle,
    string? ChildContentPath,
    string? ChildClientItemId,
    Guid? ChildDownloadClientConfigId,
    Domain.Entities.EntityKind ParentKind = Domain.Entities.EntityKind.Book,
    string? ParentOwnedMediaQuality = null,
    int ParentOwnedMediaRevision = 1,
    Guid? ParentProfileId = null,
    int ParentOwnedFormatScore = 0);

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
/// Replaces an owned single-file payload in place with a strictly-better one. Finds the single importable
/// file for the kind (a book file for <see cref="Domain.Entities.EntityKind.Book"/>, a video file for a
/// movie or single episode) under the owned folder and under the new download path, verifies the new file
/// and that both live on the same filesystem (so the swap is an atomic rename), then renames the owned file
/// aside to a <c>.prismedia-bak</c> and moves the new file into its exact path. The backup is intentionally
/// kept (it is not an importable extension, so the scanner ignores it) so the previous file is always
/// recoverable. On any failure the owned file is left exactly as it was.
/// </summary>
public interface IOwnedFileReplacer {
    /// <summary>
    /// Swaps the single owned file of <paramref name="kind"/> under <paramref name="ownedFolder"/> for the
    /// single file under <paramref name="newContentPath"/>. A different extension is always refused (it would
    /// orphan the library entity and playback/reader progress) and surfaced for manual handling.
    /// <paramref name="ownedFormatTier"/> is enforced only for book kinds (the incoming file's format tier must
    /// not regress); it is ignored for video, which re-confirms quality from the release title instead.
    /// </summary>
    Task<OwnedFileReplaceResult> ReplaceAsync(
        string ownedFolder,
        string newContentPath,
        Domain.Entities.BookFormatTier ownedFormatTier,
        CancellationToken cancellationToken,
        Domain.Entities.EntityKind kind = Domain.Entities.EntityKind.Book);
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

/// <summary>
/// The opt-in recycle bin for files Prismedia replaces. <see cref="TryMoveToBinAsync"/> returns the
/// binned path, or null when the bin is off (or the move failed) so the caller keeps its fallback.
/// </summary>
public interface IRecycleBin {
    Task<string?> TryMoveToBinAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>Purges bin entries older than the configured window; returns how many files were removed.</summary>
    Task<int> CleanupAsync(CancellationToken cancellationToken);
}

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
/// <param name="Kind">The media kind being acquired; drives per-kind enrichment and import dispatch.</param>
/// <param name="TargetLibraryRootId">The request-time import-target choice; null uses the kind's default.</param>
/// <param name="SeasonNumber">Season number for TV units; places files under the right season folder.</param>
/// <param name="EpisodeNumber">Episode number for a single-episode acquisition; names files that carry no episode token.</param>
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
    string? Description = null,
    EntityKind Kind = EntityKind.Book,
    Guid? TargetLibraryRootId = null,
    int? SeasonNumber = null,
    int? EpisodeNumber = null);
