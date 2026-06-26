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
    IReadOnlyList<string> IgnoredTerms) {
    /// <summary>Permissive defaults used when no profile is configured yet (e.g. ad-hoc verification searches).</summary>
    public static BookAcquisitionRules Default { get; } = new([], null, 1, null, null, [], []);
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

/// <summary>Book metadata captured when an acquisition is created, retained for the identify-hint handoff at import.</summary>
public sealed record AcquisitionMetadata(
    string Title,
    string? Author,
    string? Series,
    int? Year,
    string? PosterUrl,
    string? PluginId,
    string? PluginItemId,
    Guid? RequestHistoryId);

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
    string? DownloadUrl,
    string? MagnetUrl,
    string? InfoHash,
    string? InfoUrl,
    DownloadProtocol Protocol);

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
    /// <summary>When the transfer was last touched by the monitor; used as the "last seen" anchor for the removal grace period.</summary>
    DateTimeOffset UpdatedAt);

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
    Guid? DownloadClientConfigId);
