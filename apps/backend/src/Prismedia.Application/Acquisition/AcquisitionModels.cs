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
    /// Book/comic unit context: the volume this acquisition seeks, set per search by the runner (like
    /// the TV unit fields), never by a profile. Feeds <see cref="BookUnitSpecification"/> so a release
    /// declaring a DIFFERENT volume is rejected instead of winning on quality — the book analog of the
    /// wrong-season TV gate. Null outside volume-scoped searches.
    /// </summary>
    public int? VolumeNumber { get; init; }

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
    /// The intended work title for this search, set per search by the runner. Scoring uses it as a soft
    /// relevance signal so a closer title match outranks a release for a similarly named spin-off or subtitle,
    /// and the media engines' <see cref="TitleIdentitySpecification"/> uses it as an acceptance gate.
    /// Empty disables relevance scoring and the identity gates for callers that evaluate ad hoc releases
    /// without a target.
    /// </summary>
    public string? TargetTitle { get; init; }

    /// <summary>
    /// The sought work's year identity, set per search by the runner (a movie's release year; a series'
    /// premiere year — the year scene naming appends to disambiguate same-name works). Feeds
    /// <see cref="MediaYearSpecification"/> so a release naming a conflicting title-adjacent year is
    /// rejected instead of winning on quality. Null disables the year gate. Deliberately NOT consumed
    /// by the book and music engines: book years are edition-dependent and album years legitimately
    /// diverge on remasters/reissues, so a year gate would reject wanted releases there.
    /// </summary>
    public int? TargetYear { get; init; }

    /// <summary>
    /// The sought work's creator (a book's author), set per search by the runner. Feeds the book
    /// identity gate: a same-title book by a different author must be rejected, and release naming
    /// carries the author in no fixed position ("Author - Title" and "Title - Author" both occur), so
    /// the author rides separately from <see cref="TargetTitle"/> instead of being folded into it.
    /// Null/empty disables the author check.
    /// </summary>
    public string? TargetAuthor { get; init; }

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
/// <param name="VolumeNumber">Volume number for a volume-scoped book/comic acquisition; null elsewhere.</param>
/// <param name="ExternalIdentity">Optional persistent identity of the requested work in an external namespace.</param>
public sealed record AcquisitionMetadata(
    string Title,
    string? Author,
    string? Series,
    int? Year,
    string? PosterUrl,
    ExternalIdentity? ExternalIdentity,
    string? Description = null,
    EntityKind Kind = EntityKind.Book,
    Guid? EntityId = null,
    Guid? ProfileId = null,
    Guid? TargetLibraryRootId = null,
    int? SeasonNumber = null,
    int? EpisodeNumber = null,
    int? VolumeNumber = null);

/// <summary>The minimal input the background search job needs to query indexers for an acquisition.</summary>
/// <param name="Kind">The media kind being acquired; selects its query, category, and decision policy module.</param>
/// <param name="EntityId">The wanted library entity this acquisition fulfils; a wanted-linked search auto-grabs its best accepted release.</param>
/// <param name="Year">Release year context for the query ladder (year-disambiguated movie searches).</param>
/// <param name="ProfileId">The acquisition's chosen profile; its rules score the search (null = the kind's default).</param>
/// <param name="Series">Series context for TV units — the query ladder leads every rung with it.</param>
/// <param name="SeasonNumber">Season number for TV units; builds the S01 / Season 1 rungs.</param>
/// <param name="EpisodeNumber">Episode number for a single-episode acquisition; builds the S01E05 rungs.</param>
/// <param name="VolumeNumber">Volume number for a volume-scoped book/comic acquisition; gates wrong-volume releases.</param>
public sealed record AcquisitionSearchInput(
    Guid Id, string Title, string? Author, EntityKind Kind = EntityKind.Book, Guid? EntityId = null,
    int? Year = null, Guid? ProfileId = null, string? Series = null, int? SeasonNumber = null, int? EpisodeNumber = null,
    int? VolumeNumber = null) {
    /// <summary>
    /// The title of the WORK this acquisition belongs to — the series for TV units (a season or episode
    /// acquisition's own Title is "Season 1" or the episode name), the author-qualified title for music,
    /// the plain title otherwise. This is what release titles actually name, so the search's relevance
    /// scoring, identity gates, and payload validation all compare against it.
    /// </summary>
    public string WorkTitle {
        get {
            if (Kind is EntityKind.Video or EntityKind.VideoSeason or EntityKind.VideoSeries) {
                return string.IsNullOrWhiteSpace(Series) ? Title : Series;
            }

            if (Kind is EntityKind.AudioLibrary or EntityKind.AudioTrack or EntityKind.MusicArtist
                && !string.IsNullOrWhiteSpace(Author)) {
                return $"{Author} {Title}".Trim();
            }

            return Title;
        }
    }
}

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
/// <see cref="ManualPick"/> marks a release the user queued explicitly (release picker, uploaded
/// .torrent): payload validation never second-guesses a manual pick — the user is the authority, the
/// same way manual picks bypass the search-time identity gates. Older persisted snapshots default to
/// automatic, keeping validation active for them.
/// </summary>
public sealed record SelectedRelease(string Title, string? IndexerName, string? InfoHash, bool ManualPick = false) {
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
/// A monitor whose periodic action is due. <see cref="IsUpgrade"/> marks an imported Entity due for an
/// upgrade re-search (a child acquisition is spawned to search). <see cref="EntityId"/> marks a
/// Entity-only intent: containers run provider-backed child discovery, source-backed leaves remain
/// active, and fileless leaves request themselves. Entity-only dues carry no acquisition; when both ids
/// are present the acquisition lifecycle wins. <see cref="MissingChildFallback"/> marks an imported
/// child-materializing acquisition unit that left direct child phantoms wanted: <see cref="EntityId"/>
/// then carries the parent Entity and the handler requests each missing child through the shared flow.
/// </summary>
public sealed record DueMonitor(
    Guid MonitorId, Guid? AcquisitionId, string Title, bool IsUpgrade = false, Guid? EntityId = null, bool MissingChildFallback = false);

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
/// <param name="ParentEntityId">Stable Entity whose active monitor owns the upgrade lifecycle.</param>
/// <param name="ParentKind">The parent acquisition's media kind; routes the handler between the book and media replace paths.</param>
/// <param name="ParentOwnedMediaQuality">The parent's owned ladder code for a media parent; null for a book parent.</param>
/// <param name="ParentOwnedMediaRevision">The parent's owned revision for a media parent, so a same-quality higher-revision child is recognized as an upgrade at the pre-swap re-confirm gate. Defaults to 1; ignored on the book path.</param>
/// <param name="ParentProfileId">The parent's chosen profile, so the handler can re-score the child release against the same custom formats. Null uses the kind's default profile.</param>
/// <param name="ParentOwnedFormatScore">The parent's owned custom-format score, re-confirmed against at the pre-swap gate for a same-quality format-score upgrade. Defaults to 0.</param>
public sealed record UpgradeReplaceTarget(
    Guid ParentId,
    Guid? ParentEntityId,
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
    /// single file under <paramref name="newContentPath"/>. A different extension is refused by default (it
    /// would orphan the library entity and playback/reader progress) and surfaced for manual handling;
    /// <paramref name="allowFormatChange"/> — the user's explicit "import anyway" — installs the new file
    /// under the owned file's basename with the incoming extension and retires the old file (recycle bin
    /// when configured), trusting the follow-up scan to re-bind the entity to the new path.
    /// <paramref name="ownedFormatTier"/> is enforced only for book kinds (the incoming file's format tier must
    /// not regress); it is ignored for video, which re-confirms quality from the release title instead.
    /// </summary>
    Task<OwnedFileReplaceResult> ReplaceAsync(
        string ownedFolder,
        string newContentPath,
        Domain.Entities.BookFormatTier ownedFormatTier,
        CancellationToken cancellationToken,
        Domain.Entities.EntityKind kind = Domain.Entities.EntityKind.Book,
        bool allowFormatChange = false);

    /// <summary>
    /// Performs the same swap while retaining the recoverable backup sidecar after success. Durable TV
    /// checkpoints use this form with attempt-specific incoming-byte evidence so a process restart can
    /// distinguish an installed same-path upgrade from the untouched old file before FinalPath is durable.
    /// </summary>
    Task<OwnedFileReplaceResult> ReplaceRetainingBackupAsync(
        string ownedFolder,
        string newContentPath,
        Domain.Entities.BookFormatTier ownedFormatTier,
        CancellationToken cancellationToken,
        Domain.Entities.EntityKind kind = Domain.Entities.EntityKind.Book,
        bool allowFormatChange = false,
        string? recoveryBackupPath = null,
        string? incomingEvidencePath = null) =>
        ReplaceAsync(ownedFolder, newContentPath, ownedFormatTier, cancellationToken, kind, allowFormatChange);
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

/// <summary>
/// Serializes one remote download-client Add with persistence of its transfer pointer. The production
/// implementation holds the acquisition row lock until the pointer commits, so teardown either wins before
/// Add starts or waits and then observes the exact remote item it must remove.
/// </summary>
public interface IAcquisitionTransferAddCoordinator {
    /// <summary>
    /// Acquires ownership only while the acquisition is still in the queue-preparation state; null means a
    /// concurrent lifecycle operation won and no remote Add may be attempted.
    /// </summary>
    Task<IAcquisitionTransferAddLease?> AcquireAsync(Guid acquisitionId, CancellationToken cancellationToken);
}

/// <summary>One acquisition-row lock spanning remote Add through durable transfer-pointer commit.</summary>
public interface IAcquisitionTransferAddLease : IAsyncDisposable {
    /// <summary>Commits the transaction after the transfer pointer has been persisted.</summary>
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>Minimal transfer wiring for an acquisition: its status, imported location, and download-client item.</summary>
public sealed record AcquisitionTransferInfo(
    AcquisitionStatus Status,
    string? FinalSourcePath,
    string? ClientItemId,
    Guid? DownloadClientConfigId,
    string? Category = null,
    string? State = null);

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

/// <summary>
/// Durable completion ticket for an acquisition whose download finished before its import or replacement
/// job was safely queued. <see cref="Kind"/> determines whether an ordinary acquisition has a registered
/// import engine; <see cref="IsUpgrade"/> routes upgrade children to the in-place replacement workflow.
/// </summary>
public sealed record DownloadedAcquisitionCompletion(
    Guid AcquisitionId,
    EntityKind Kind,
    bool IsUpgrade);

/// <summary>Everything the import job needs: the captured metadata, the chosen profile, and the completed download's location.</summary>
/// <param name="Kind">The media kind being acquired; drives per-kind enrichment and import dispatch.</param>
/// <param name="TargetLibraryRootId">The request-time import-target choice; null uses the kind's default.</param>
/// <param name="SeasonNumber">Season number for TV units; places files under the right season folder.</param>
/// <param name="EpisodeNumber">Episode number for a single-episode acquisition; names files that carry no episode token.</param>
/// <param name="EntityId">The wanted/monitored library entity this acquisition fulfills; an entity that already lives on disk redirects the import into its existing folder.</param>
/// <param name="ExternalIdentity">Optional persistent identity carried from request through enrichment and import.</param>
/// <param name="FinalSourcePath">Final imported source boundary, or a legacy placed-file recovery path from before typed checkpoints.</param>
public sealed record AcquisitionImportContext(
    Guid Id,
    string Title,
    string? Author,
    string? Series,
    int? Year,
    string? PosterUrl,
    ExternalIdentity? ExternalIdentity,
    Guid? ProfileId,
    string? ContentPath,
    string? ClientItemId,
    Guid? DownloadClientConfigId,
    string? Description = null,
    EntityKind Kind = EntityKind.Book,
    Guid? TargetLibraryRootId = null,
    int? SeasonNumber = null,
    int? EpisodeNumber = null,
    Guid? EntityId = null,
    string? FinalSourcePath = null,
    TvImportCheckpoint? TvImportCheckpoint = null,
    ImportPlacementCheckpoint? ImportPlacementCheckpoint = null) {
    /// <summary>
    /// The user's explicit "import anyway": an upgrade that changes the file extension — normally held
    /// for manual import — replaces the owned file across formats. Carried by the manual retry-import
    /// job payload only; automatic imports never set it.
    /// </summary>
    public bool AllowFormatChange { get; init; }
}

/// <summary>
/// Durable, kind-neutral placement plan for a book, movie, or album import. The plan reserves every
/// exact library target before the first filesystem mutation and advances one unit at a time. A retry
/// therefore resumes the same paths even when Move consumed the payload, while Copy and Hardlink can
/// recognize already-published bytes instead of producing a collision suffix.
/// </summary>
/// <param name="Kind">Entity kind whose import engine owns the plan.</param>
/// <param name="LibraryRootId">Configured library root that owns every target.</param>
/// <param name="LibraryRootPath">Absolute root boundary captured when the plan was created.</param>
/// <param name="PayloadRootPath">Absolute completed-download boundary that owns every source.</param>
/// <param name="ImportMode">Move/copy/hardlink behavior captured from the acquisition profile.</param>
/// <param name="HintPath">Exact path-keyed identify hint boundary used by the kind's scanner.</param>
/// <param name="FinalSourcePath">Exact source boundary published on the acquisition after placement.</param>
/// <param name="SuccessMessage">Terminal user-facing import summary retained across retries.</param>
/// <param name="Units">Every payload file the engine will mutate, including non-media companions.</param>
/// <param name="TransferClientItemId">Download-client item this plan was built from; another transfer may never reuse it.</param>
/// <param name="AttemptId">Opaque token identifying this exact placement attempt.</param>
/// <param name="ClaimJobId">Queue job exclusively allowed to advance this attempt.</param>
public sealed record ImportPlacementCheckpoint(
    EntityKind Kind,
    Guid LibraryRootId,
    string LibraryRootPath,
    string PayloadRootPath,
    ImportMode ImportMode,
    string HintPath,
    string FinalSourcePath,
    string SuccessMessage,
    IReadOnlyList<ImportPlacementCheckpointUnit> Units,
    string? TransferClientItemId = null,
    Guid AttemptId = default,
    Guid ClaimJobId = default);

/// <summary>One exact payload-to-library placement in a kind-neutral durable import plan.</summary>
/// <param name="SourceRelativePath">Download-payload-relative source path retained for diagnostics and validation.</param>
/// <param name="SourceAbsolutePath">Exact original payload path used for placement and crash recovery.</param>
/// <param name="TargetAbsolutePath">Exact collision-resolved target reserved before any mutation.</param>
/// <param name="IsMedia">Whether this file participates in Entity materialization readiness.</param>
/// <param name="FinalPath">Target path after the mutation is durably checkpointed; null while pending.</param>
public sealed record ImportPlacementCheckpointUnit(
    string SourceRelativePath,
    string SourceAbsolutePath,
    string TargetAbsolutePath,
    bool IsMedia,
    string? FinalPath = null);

/// <summary>
/// Durable execution plan for a TV import. It is written before the first filesystem mutation and
/// updated after every placed file, so a retry can finish the original plan without guessing from
/// whatever files remain in the download directory.
/// </summary>
/// <param name="LibraryRootId">Video root that owns every target.</param>
/// <param name="SeriesFolderPath">Absolute series folder used by Entity hierarchy materialization.</param>
/// <param name="ImportMode">Move/copy/hardlink behavior selected when the plan was created.</param>
/// <param name="AllowFormatChange">The user's explicit cross-format replacement consent, retained for retries.</param>
/// <param name="SuccessMessage">Terminal user-facing import summary retained across retries.</param>
/// <param name="PreferSingleFileFinalSource">Whether a one-file result should checkpoint the file rather than its season folder.</param>
/// <param name="Units">Every file mutation required for the successful import.</param>
/// <param name="TransferClientItemId">Download-client item this plan was built from; a later transfer may never reuse it.</param>
/// <param name="AttemptId">Opaque token that scopes recovery artifacts to this exact placement plan.</param>
/// <param name="ClaimJobId">Queue job exclusively allowed to execute this attempt; reassigned only by an atomic retry claim.</param>
public sealed record TvImportCheckpoint(
    Guid LibraryRootId,
    string SeriesFolderPath,
    ImportMode ImportMode,
    bool AllowFormatChange,
    string SuccessMessage,
    bool PreferSingleFileFinalSource,
    IReadOnlyList<TvImportCheckpointUnit> Units,
    string? TransferClientItemId = null,
    Guid AttemptId = default,
    Guid ClaimJobId = default);

/// <summary>One durable TV file placement and its exact Entity position.</summary>
/// <param name="SourceRelativePath">Download-payload-relative source path.</param>
/// <param name="SourceAbsolutePath">Exact original payload path retained for replacement-stage recovery.</param>
/// <param name="TargetAbsolutePath">Exact absolute library target, collision-resolved before any mutation.</param>
/// <param name="SeasonNumber">Season position represented by the file.</param>
/// <param name="EpisodeNumber">Primary episode position represented by the file.</param>
/// <param name="CoveredEpisodeNumbers">Additional episode positions satisfied by the same file.</param>
/// <param name="PreviousFilePath">Owned source replaced by an upgrade, or null for a new placement.</param>
/// <param name="FinalPath">Actual placed path once the mutation completes; null while still pending.</param>
/// <param name="ReplacementBackupPath">Attempt-specific retained copy of the pre-upgrade bytes.</param>
/// <param name="ReplacementEvidencePath">Attempt-specific incoming-byte evidence retained until FinalPath is durable.</param>
public sealed record TvImportCheckpointUnit(
    string SourceRelativePath,
    string TargetAbsolutePath,
    int SeasonNumber,
    int EpisodeNumber,
    IReadOnlyList<int> CoveredEpisodeNumbers,
    string? PreviousFilePath = null,
    string? FinalPath = null,
    string? SourceAbsolutePath = null,
    string? ReplacementBackupPath = null,
    string? ReplacementEvidencePath = null);

/// <summary>
/// Stable sidecar paths used by the crash-safe owned-file replacement protocol. They deliberately use
/// non-media suffixes so scans never import an in-progress or recoverable copy as another Entity.
/// </summary>
public static class OwnedFileReplacementArtifacts {
    public const string BackupSuffix = ".prismedia-bak";
    public const string StagedSuffix = ".prismedia-new";

    public static string BackupPath(string ownedPath) => Path.GetFullPath(ownedPath) + BackupSuffix;
    public static string StagedPath(string ownedPath) => Path.GetFullPath(ownedPath) + StagedSuffix;
    public static string CheckpointBackupPath(string ownedPath, Guid attemptId) =>
        Path.GetFullPath(ownedPath) + $".prismedia-bak-{attemptId:N}";
    public static string CheckpointEvidencePath(string ownedPath, Guid attemptId) =>
        Path.GetFullPath(ownedPath) + $".prismedia-incoming-{attemptId:N}";
}
