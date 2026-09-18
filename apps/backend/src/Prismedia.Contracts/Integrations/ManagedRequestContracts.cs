using Prismedia.Domain.Entities;
using Prismedia.Contracts.Requests;
using System.Text.Json.Serialization;

namespace Prismedia.Contracts.Integrations;

/// <summary>Previews an existing wanted identity and a configured read-only external library boundary.</summary>
public sealed record PreviewManagedRequestInput(Guid EntityId, Guid LibraryRootId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null);
/// <summary>Exact reviewed work, mapped root, remote choices, and any existing external holding.</summary>
public sealed record ManagedRequestPreview(Guid EntityId, string Title, ManagedLookupInput Work,
    ExternalLibraryMount Mount, ManagerOptions Options, ManagedItemSnapshot? Existing,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null);
/// <summary>Explicit fulfillment intent. Profile applies to initial creation; existing holdings must already use that reviewed profile.</summary>
public sealed record CreateManagedRequestInput(Guid OperationId, Guid EntityId, Guid LibraryRootId,
    ManagedLookupInput ReviewedWork, string ProfileId, bool Monitored, bool Search,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null);
/// <summary>Durable request progress; completion requires local files, independently of manager search progress.</summary>
public sealed record ManagedRequestResponse(Guid Id, Guid ConnectionId, Guid EntityId, Guid LibraryRootId,
    string Title, ManagedRequestPhase Phase, long Revision, string? RemoteId, bool Monitored, bool Search,
    bool ReviewRequired, bool CanCancel, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Problem,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null);
/// <summary>Revision-fenced cancellation of intent that has no possible creation effect.</summary>
public sealed record CancelManagedRequestInput(long ExpectedRevision);

/// <summary>Reviews one metadata selection and its external-manager choices without creating local entities or remote work.</summary>
public sealed record ReviewManagedRequestInput(
    Guid? LibraryRootId,
    ReviewedRequestCommitRequest Request,
    long? ManagerDiscoveryRevision = null);

/// <summary>A canonical metadata selection paired with current mapped-library, profile, and manager lookup evidence.</summary>
public sealed record ReviewedManagedRequest(
    long ConnectionRevision,
    long? ManagerDiscoveryRevision,
    ReviewedRequestCommitRequest Request,
    string Title,
    ManagedLookupInput Work,
    ExternalLibraryMount Mount,
    ManagerOptions Options,
    ManagedItemSnapshot? Existing,
    IReadOnlyList<ReviewedFulfillmentOwnership> ExistingFulfillments);

/// <summary>
/// Existing Prismedia ownership of canonical reviewed work. A missing owner kind identifies native
/// Prismedia ownership; finite series scopes list only the selected episodes that already have an owner.
/// </summary>
/// <param name="HasLocalSource">True only when every represented movie or episode retains a local source.</param>
public sealed record ReviewedFulfillmentOwnership(
    Guid EntityId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FulfillmentOwnerKind? OwnerKind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ConnectionId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ConnectionName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? RequestId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManagedRequestPhase? RequestPhase,
    bool HasLocalSource);

/// <summary>Atomically saves reviewed metadata and accepts durable external-manager fulfillment intent.</summary>
public sealed record CommitReviewedManagedRequestInput(
    Guid OperationId,
    long ExpectedConnectionRevision,
    Guid LibraryRootId,
    string ProfileId,
    bool Monitored,
    bool Search,
    ReviewedRequestCommitRequest Request,
    long? ManagerDiscoveryRevision = null);

/// <summary>The resulting library entity and optional durable manager request when selected work still needs files.</summary>
public sealed record ReviewedManagedRequestCommitResponse(
    Guid EntityId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManagedRequestResponse? ManagedRequest);
