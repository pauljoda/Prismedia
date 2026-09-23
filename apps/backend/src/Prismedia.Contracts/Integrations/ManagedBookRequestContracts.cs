using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using System.Text.Json.Serialization;

namespace Prismedia.Contracts.Integrations;

/// <summary>One exact Book format and its mapped manager library.</summary>
public sealed record ManagedBookRenditionChoice(BookRendition Rendition, Guid LibraryRootId, bool Search);

/// <summary>Reviews either an existing Book or one metadata-selected work before manager acceptance.</summary>
public sealed record ReviewManagedBookRequestInput(
    Guid? EntityId,
    ReviewedRequestCommitRequest? Request,
    IReadOnlyList<ManagedBookRenditionChoice> Renditions);

/// <summary>Read-only manager evidence for one selected Book format.</summary>
public sealed record ReviewedManagedBookRendition(BookRendition Rendition, ManagedLookupInput Work,
    ExternalLibraryMount Mount, ManagerOptions Options, ManagedItemSnapshot? Existing);

/// <summary>One work and independently reviewed ebook and audiobook manager destinations.</summary>
public sealed record ReviewedManagedBookRequest(long ConnectionRevision, string Title,
    IReadOnlyList<ReviewedManagedBookRendition> Renditions);

/// <summary>One stable operation that accepts selected Book formats through a connected manager.</summary>
public sealed record CommitManagedBookRequestInput(Guid OperationId, long ExpectedConnectionRevision,
    Guid? EntityId, ReviewedRequestCommitRequest? Request,
    IReadOnlyList<ManagedBookRenditionChoice> Renditions);

/// <summary>Acceptance or an actionable retry outcome for one selected Book format.</summary>
public sealed record ManagedBookRenditionResult(BookRendition Rendition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManagedRequestResponse? Request = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error = null);

/// <summary>The common Book identity and independent durable manager request outcomes.</summary>
public sealed record CommitManagedBookRequestResponse(Guid EntityId,
    IReadOnlyList<ManagedBookRenditionResult> Renditions);
