namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of lifecycle states for a first-party acquisition. An acquisition captures the
/// intent to obtain a book, searches indexers for releases, downloads the chosen release through
/// a download client, then runs the media kind's import workflow against a library root.
/// </summary>
public enum AcquisitionStatus {
    /// <summary>Created from a request but no release search has run yet.</summary>
    [Code("pending")]
    Pending,

    /// <summary>An indexer search is currently running for this acquisition.</summary>
    [Code("searching")]
    Searching,

    /// <summary>Releases were found and scored; the user must pick one to download.</summary>
    [Code("awaiting-selection")]
    AwaitingSelection,

    /// <summary>A release was sent to the download client and is queued there.</summary>
    [Code("queued")]
    Queued,

    /// <summary>The download client is actively transferring the chosen release.</summary>
    [Code("downloading")]
    Downloading,

    /// <summary>The download completed and the payload is ready to import.</summary>
    [Code("downloaded")]
    Downloaded,

    /// <summary>The completed payload is being moved into the target library root.</summary>
    [Code("importing")]
    Importing,

    /// <summary>The payload was placed in the library and its media-specific import workflow completed.</summary>
    [Code("imported")]
    Imported,

    /// <summary>
    /// A destructive workflow has durably claimed this acquisition and cancelled its background work.
    /// The persisted teardown intent determines whether completion removes it or replaces it with a retry.
    /// </summary>
    [Code("stopping")]
    Stopping,

    /// <summary>The acquisition failed; the status message carries the reason and it can be retried.</summary>
    [Code("failed")]
    Failed,

    /// <summary>The acquisition was cancelled by the user.</summary>
    [Code("cancelled")]
    Cancelled,

    /// <summary>The completed payload could not be imported automatically and needs manual resolution.</summary>
    [Code("manual-import-required")]
    ManualImportRequired
}

/// <summary>Durable completion intent captured when destructive acquisition cleanup begins.</summary>
public enum AcquisitionTeardownIntent {
    /// <summary>Remove the acquisition after its remote transfer and owned library files are gone.</summary>
    [Code("remove")]
    Remove,

    /// <summary>Replace the acquisition with a clean pending search after owned library files are gone.</summary>
    [Code("reacquire")]
    Reacquire
}

/// <summary>Prismedia-owned sentinel states stored on a transfer before client-native telemetry exists.</summary>
public enum TransferOwnershipState {
    /// <summary>
    /// A durable ownership placeholder exists and the remote Add is either in flight or awaiting crash
    /// recovery. Teardown must resolve it by correlation before deleting the acquisition owner.
    /// </summary>
    [Code("adding")]
    Adding
}

/// <summary>
/// Closed set of reasons the decision engine rejects a release candidate. A rejected candidate is
/// still surfaced to the user with its reason so the choice is transparent rather than silently hidden.
/// </summary>
public enum ReleaseRejectionReason {
    /// <summary>The release does not appear to contain a supported book payload.</summary>
    [Code("unsupported-format")]
    UnsupportedFormat,

    /// <summary>The torrent has fewer seeders than the profile's minimum.</summary>
    [Code("below-min-seeders")]
    BelowMinSeeders,

    /// <summary>The release size falls outside the profile's allowed range.</summary>
    [Code("size-out-of-range")]
    SizeOutOfRange,

    /// <summary>A profile-required term is missing from the release title.</summary>
    [Code("missing-required-term")]
    MissingRequiredTerm,

    /// <summary>A profile-ignored term is present in the release title.</summary>
    [Code("has-ignored-term")]
    HasIgnoredTerm,

    /// <summary>The release language does not match the profile's required language.</summary>
    [Code("language-mismatch")]
    LanguageMismatch,

    /// <summary>The release uses a protocol Prismedia does not acquire in v1 (e.g. usenet).</summary>
    [Code("wrong-protocol")]
    WrongProtocol,

    /// <summary>The release has no usable download or magnet link (e.g. a meta-search info-page result).</summary>
    [Code("no-download-link")]
    NoDownloadLink,

    /// <summary>The release's identity is on the acquisition blocklist (a prior attempt failed or it was manually blocked).</summary>
    [Code("blocklisted")]
    Blocklisted,

    /// <summary>The release's detected quality is below the profile's minimum-quality floor on some axis.</summary>
    [Code("quality-not-allowed")]
    QualityNotAllowed,

    /// <summary>An upgrade search: the candidate is not a strict improvement over the owned copy, so it would not upgrade it.</summary>
    [Code("not-an-upgrade")]
    NotAnUpgrade,

    /// <summary>An upgrade search: the candidate would downgrade the format relative to the owned copy, even if another axis improves.</summary>
    [Code("format-downgrade")]
    FormatDowngrade,

    /// <summary>A TV search: the release names a different unit than the one sought (wrong season/episode, or a single episode when a season pack is wanted).</summary>
    [Code("wrong-tv-unit")]
    WrongTvUnit,

    /// <summary>The release's total custom-format score is below the profile's minimum-format-score floor.</summary>
    [Code("below-min-format-score")]
    BelowMinFormatScore,

    /// <summary>The release title names an executable/dangerous file (e.g. ends in .exe or .scr) — the classic fake-release payload.</summary>
    [Code("dangerous-content")]
    DangerousContent,

    /// <summary>The release's leading title tokens do not name exactly the sought work (a sequel, spin-off, or different title sharing a prefix).</summary>
    [Code("title-mismatch")]
    TitleMismatch,

    /// <summary>The release's title-adjacent year names a different same-name work (a remake or reboot from another year).</summary>
    [Code("wrong-year")]
    WrongYear,

    /// <summary>A book search: the release names a different volume than the one sought.</summary>
    [Code("wrong-volume")]
    WrongVolume
}

/// <summary>
/// Closed set of strategies for placing a completed payload into the target library root.
/// </summary>
public enum ImportMode {
    /// <summary>Move the payload out of the download directory into the library root.</summary>
    [Code("move")]
    Move,

    /// <summary>Copy the payload into the library root, leaving the download in place.</summary>
    [Code("copy")]
    Copy,

    /// <summary>
    /// Hardlink the payload into the library root when both share a filesystem (instant, no extra
    /// space, the torrent keeps seeding from the download dir), transparently falling back to copy
    /// across volumes. The Sonarr "copy using hardlinks" behavior.
    /// </summary>
    [Code("hardlink")]
    Hardlink
}
