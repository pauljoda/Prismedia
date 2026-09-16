using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Contracts.Integrations;

/// <summary>Finite executor request, pinned to an inspected source selection and bounded output budget.</summary>
public sealed record SubmitTransferInput(Guid ClientOperationId, string Url, string SelectionId, string SelectionRevision,
    IReadOnlyList<string> ItemIds, int MaximumItems, long MaximumBytes);

/// <summary>Recovers a potentially accepted submission using its durable local operation identity.</summary>
public sealed record FindTransferInput(Guid ClientOperationId);

/// <summary>Lookup result. A missing reference means no accepted operation was found, never successful fulfillment.</summary>
public sealed record FindTransferResult(RemoteTransferSnapshot? Job);

/// <summary>One structured execution failure for an explicitly selected item.</summary>
public sealed record RemoteTransferItemFailure(string ItemId, string Message);

/// <summary>Authoritative snapshot correlated to an operation, installation, and stable job; terminal success identifies sealed outputs.</summary>
public sealed record RemoteTransferSnapshot(string InstanceId, string JobId, Guid ClientOperationId, long Revision,
    RemoteJobState State, double? Progress, string? ManifestRevision, DateTimeOffset? RetainedUntil,
    IReadOnlyList<RemoteTransferItemFailure> ItemFailures, string? Message = null,
    DateTimeOffset? NextPollAfter = null, bool ArtifactsExpired = false);

/// <summary>Reads or cancels one exact job on the connection's bound remote installation.</summary>
public sealed record RemoteTransferJobInput(string JobId);

/// <summary>Reads one stable page of the requested sealed manifest revision.</summary>
public sealed record ReadTransferManifestInput(string JobId, string Revision, string? Cursor = null, int Limit = 100);

/// <summary>Adapter manifest page. The host assembles all pages and validates the sealed count before using the output set.</summary>
public sealed record TransferManifestPage(string JobId, string Revision, bool Sealed, int ArtifactCount,
    IReadOnlyList<IntegrationArtifact> Artifacts, string? NextCursor = null);

/// <summary>Obtains short-lived byte authorization for an artifact from an exact sealed revision.</summary>
public sealed record AuthorizeTransferArtifactInput(string JobId, string Revision, string ArtifactId);

/// <summary>Requests guaranteed retention while local retrieval or import remains unfinished.</summary>
public sealed record RenewTransferRetentionInput(string JobId, DateTimeOffset RetainUntil);

/// <summary>A remote retention guarantee, which must be checked before long transfers and import retries.</summary>
public sealed record TransferRetentionResult(string JobId, DateTimeOffset RetainedUntil);

/// <summary>Stable import receipt persisted locally before acknowledgement; its artifact hashes cannot change between retries.</summary>
public sealed record AcknowledgeTransferInput(string JobId, string ManifestRevision, Guid ReceiptId,
    IReadOnlyList<IntegrationArtifactImport> Artifacts);

/// <summary>Remote confirmation of the exact receipt, independent of local byte transfer or import.</summary>
public sealed record TransferAcknowledgement(string JobId, Guid ReceiptId, bool Accepted);

/// <summary>Inspects an arbitrary source URL without submitting work or assuming search support.</summary>
public sealed record InspectTransferInput(string Url, EntityKind EntityKind, int MaximumItems = 100);

/// <summary>Finite source choice returned by an executor's inspect operation.</summary>
public sealed record InspectedTransferItem(string Id, string Title, EntityKind EntityKind);

/// <summary>Frozen selection evidence; submission must preserve this revision or reject it as stale.</summary>
public sealed record TransferInspection(string SelectionId, string Revision, DateTimeOffset ExpiresAt,
    string CanonicalUrl, IReadOnlyList<InspectedTransferItem> Items, bool RequiresSelection, IReadOnlyList<string> Warnings);
