using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Integrations;

/// <summary>A bounded title search of one external manager's upstream catalog.</summary>
public sealed record ManagedDiscoveryQuery(EntityKind EntityKind, string Query, int Limit = 25);

/// <summary>A normalized person credit supplied by an external manager during exact metadata review.</summary>
public sealed record ManagedPersonCredit(
    string Name,
    CreditRole Role,
    string? Character,
    int? SortOrder,
    IReadOnlyDictionary<string, string>? ExternalIds = null,
    string? ProfileUrl = null);

/// <summary>Optional normalized descriptive metadata supplied by an external manager catalog.</summary>
public sealed record ManagedDiscoveryMetadata(
    string? OriginalTitle = null,
    string? Overview = null,
    string? Studio = null,
    string? Classification = null,
    int? RuntimeMinutes = null,
    decimal? Rating = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyDictionary<string, string>? Dates = null,
    IReadOnlyList<string>? Urls = null,
    string? PosterUrl = null,
    string? BackdropUrl = null,
    IReadOnlyList<ManagedPersonCredit>? Credits = null);

/// <summary>Manager-confirmed catalog candidate. External ids are evidence from the selected connection.</summary>
public sealed record ManagedDiscoveryCandidate(
    EntityKind EntityKind,
    string Title,
    int? Year,
    IReadOnlyDictionary<string, string> ExternalIds,
    ManagedDiscoveryMetadata? Metadata = null);

/// <summary>Bounded manager catalog results before the host chooses a canonical request identity.</summary>
public sealed record ManagedDiscoveryPage(IReadOnlyList<ManagedDiscoveryCandidate> Items);

/// <summary>Host-projected discovery result with the exact identity clients must use for review.</summary>
public sealed record ManagedDiscoverySearchResult(
    EntityKind EntityKind,
    string Title,
    int? Year,
    ExternalIdentity ExternalIdentity,
    ManagedDiscoveryMetadata? Metadata = null);

/// <summary>Connection-scoped manager catalog results.</summary>
public sealed record ManagedDiscoverySearchResponse(IReadOnlyList<ManagedDiscoverySearchResult> Items);

/// <summary>Requests fresh review metadata for one exact manager-discovered identity.</summary>
public sealed record ManagedDiscoveryReviewRequest(EntityKind EntityKind, ExternalIdentity ExternalIdentity);

/// <summary>A canonical request review pinned to the selected connection configuration.</summary>
public sealed record ManagedDiscoveryReviewResponse(
    long ConnectionRevision,
    RequestReviewResponse Review,
    ManagedDiscoveryMetadata? Metadata = null);

/// <summary>Prepares a wanted movie only after refreshing the exact manager review and connection revision.</summary>
public sealed record PrepareManagedDiscoveryRequest(long ConnectionRevision, ReviewedRequestCommitRequest Request);
