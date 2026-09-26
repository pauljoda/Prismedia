using Prismedia.Domain.Entities;
using Prismedia.Contracts.Requests;
using System.Text.Json.Serialization;

namespace Prismedia.Contracts.Integrations;

/// <summary>Exact reviewed work, mapped root, remote choices, and any existing external holding.</summary>
public sealed record ManagedRequestPreview(Guid EntityId, string Title, ManagedLookupInput Work,
    ExternalLibraryMount Mount, ManagerOptions Options, ManagedItemSnapshot? Existing,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null);
/// <summary>Explicit fulfillment intent. Profile applies to initial creation; existing holdings must already use that reviewed profile.</summary>
public sealed record CreateManagedRequestInput(Guid OperationId, Guid EntityId, Guid LibraryRootId,
    ManagedLookupInput ReviewedWork, string? ProfileId, bool Monitored, bool Search,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null);
/// <summary>Durable request progress; completion requires local files, independently of manager search progress.</summary>
public sealed record ManagedRequestResponse(Guid Id, Guid ConnectionId, Guid EntityId, Guid LibraryRootId,
    string Title, ManagedRequestPhase Phase, long Revision, string? RemoteId, bool Monitored, bool Search,
    bool ReviewRequired, bool CanCancel, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Problem,
    Guid HoldingId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null);
/// <summary>Revision-fenced cancellation of intent that has no possible creation effect.</summary>
public sealed record CancelManagedRequestInput(long ExpectedRevision);

/// <summary>
/// One fulfillment scope of a managed request: the whole work, or one independently fulfilled rendition of
/// it, and the mapped library that receives its files. A review may leave the library unchosen so the
/// server proposes the mapping that already contains the holding; a commit always names it.
/// </summary>
/// <param name="Rendition">The rendition this scope fulfills; null for kinds without independent renditions.</param>
/// <param name="LibraryRootId">Mapped external library that receives files; required at commit.</param>
public sealed record ManagedRequestScopeChoice(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BookRendition? Rendition = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? LibraryRootId = null);

/// <summary>One exact missing target chosen from a holding the connected manager already has.</summary>
/// <param name="Item">The connected holding, as the manager identifies it.</param>
/// <param name="RemoteTargetId">The manager's identifier of the chosen target inside that holding.</param>
/// <param name="TargetLabel">The exact target designation the person reviewed, such as an issue number.</param>
public sealed record ManagedConnectedTargetInput(ManagedItemInput Item, string RemoteTargetId, string TargetLabel);

/// <summary>
/// Reviews one managed request without creating local entities or remote work. Exactly one source names the
/// work: a reviewed metadata selection, an existing wanted Entity, or a target inside a connected holding.
/// </summary>
/// <param name="Scopes">The requested fulfillment scopes; one per rendition for kinds with independent renditions, otherwise exactly one.</param>
/// <param name="EntityId">An existing wanted Entity, when the work already exists locally.</param>
/// <param name="TargetEntityIds">Finite child scope of an existing container, or null for the whole work.</param>
/// <param name="Request">A reviewed metadata selection for work that may not exist locally yet.</param>
/// <param name="ManagerDiscoveryRevision">The manager discovery revision the selection came from, when it did.</param>
/// <param name="Connected">A missing target inside a holding the manager already has.</param>
public sealed record ReviewManagedRequestInput(
    IReadOnlyList<ManagedRequestScopeChoice> Scopes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? EntityId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReviewedRequestCommitRequest? Request = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ManagerDiscoveryRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManagedConnectedTargetInput? Connected = null);

/// <summary>One exact target the request names, with the manager's own evidence for it.</summary>
public sealed record ReviewedManagedTarget(string RemoteId, string Label, string Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? ExternalIds = null);

/// <summary>Current manager, library, and ownership evidence for one reviewed fulfillment scope.</summary>
/// <param name="Rendition">The rendition this scope fulfills; null for kinds without independent renditions.</param>
/// <param name="Work">Exact manager lookup identity of the work for this scope.</param>
/// <param name="Mount">Mapped library that would receive files.</param>
/// <param name="Options">The manager's current profile and root choices for this scope.</param>
/// <param name="Existing">The manager's current holding of the work, when it has one.</param>
/// <param name="ExistingFulfillments">Prismedia ownership already covering the work.</param>
/// <param name="Expansion">An exact reviewed append to one retained finite-container holding.</param>
/// <param name="ExistingRequest">A retained request on this connection that already covers the work.</param>
/// <param name="TargetEntityIds">Local targets an existing container scope names, when it names any.</param>
public sealed record ReviewedManagedRequestScope(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BookRendition? Rendition,
    ManagedLookupInput Work,
    ExternalLibraryMount Mount,
    ManagerOptions Options,
    ManagedItemSnapshot? Existing,
    IReadOnlyList<ReviewedFulfillmentOwnership> ExistingFulfillments,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManagedRequestExpansion? Expansion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManagedRequestResponse? ExistingRequest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null);

/// <summary>A reviewed work, its source, and current evidence for every requested scope.</summary>
/// <param name="ConnectionRevision">Connection revision the evidence was read at; a commit must present it.</param>
/// <param name="ManagerDiscoveryRevision">The manager discovery revision the selection came from, when it did.</param>
/// <param name="Title">Display title of the reviewed work.</param>
/// <param name="Scopes">One reviewed scope per requested scope, in request order.</param>
/// <param name="EntityId">The existing local Entity, when the work was named by one.</param>
/// <param name="Request">The canonical metadata selection, when the work was named by one.</param>
/// <param name="Connected">The connected target, when the work was named by one.</param>
/// <param name="Targets">Exact targets a connected source names, with the manager's evidence.</param>
public sealed record ReviewedManagedRequest(
    long ConnectionRevision,
    long? ManagerDiscoveryRevision,
    string Title,
    IReadOnlyList<ReviewedManagedRequestScope> Scopes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? EntityId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReviewedRequestCommitRequest? Request = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManagedConnectedTargetInput? Connected = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ReviewedManagedTarget>? Targets = null);

/// <summary>An exact reviewed append to one retained finite-series holding.</summary>
public sealed record ManagedRequestExpansion(
    Guid HoldingId,
    IReadOnlyList<Guid> RetainedTargetEntityIds,
    int SelectedOwnedTargetCount,
    int NewTargetCount);

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

/// <summary>
/// Atomically saves the reviewed work and accepts durable external-manager fulfillment intent for every
/// requested scope under one stable operation identity. The same source rules as the review apply.
/// </summary>
/// <param name="OperationId">Stable identity that makes retries of this exact request idempotent.</param>
/// <param name="ExpectedConnectionRevision">Connection revision the review was read at.</param>
/// <param name="Scopes">The requested scopes; every scope names its mapped library.</param>
/// <param name="ProfileId">Manager quality profile for kinds that use one; null otherwise.</param>
/// <param name="Monitored">Reviewed monitoring choice, where the kind allows a choice.</param>
/// <param name="Search">Whether the manager is asked to search now.</param>
public sealed record CommitReviewedManagedRequestInput(
    Guid OperationId,
    long ExpectedConnectionRevision,
    IReadOnlyList<ManagedRequestScopeChoice> Scopes,
    string? ProfileId,
    bool Monitored,
    bool Search,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? EntityId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReviewedRequestCommitRequest? Request = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ManagerDiscoveryRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManagedConnectedTargetInput? Connected = null);

/// <summary>
/// The outcome of one requested scope: its durable manager request, nothing when the work already has its
/// files, or an actionable error when this scope alone was not accepted and can be retried.
/// </summary>
public sealed record ManagedRequestScopeResult(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BookRendition? Rendition,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? TargetEntityIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManagedRequestResponse? ManagedRequest,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error = null);

/// <summary>The resulting library Entity and the outcome of every requested scope, in request order.</summary>
public sealed record ReviewedManagedRequestCommitResponse(
    Guid EntityId,
    IReadOnlyList<ManagedRequestScopeResult> Scopes);
