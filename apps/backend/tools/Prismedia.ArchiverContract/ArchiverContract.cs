namespace Prismedia.Archiver;

/// <summary>Independent Archiver HTTP vocabulary. No Prismedia domain, database, or entity schema is required.</summary>
public static class ArchiverWire {
    public const string ApiVersion = "1.0";
    public const string Gallery = "gallery";
    public const string GalleryProfile = "ordered-gallery";
    public const string ImageSet = "image-set";
    public const string Image = "image";
    public const string ImageProfile = "single-image";
    public const string Png = "png";
    public const string Book = "book";
    public const string Comic = "comic";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Waiting = "waiting";
    public const string Succeeded = "succeeded";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Content = "content";
    public const string PublicationProfile = "single-publication";
    public const string Epub = "epub";
    public const string Cbz = "cbz";
    public const string Inspect = "inspect";
    public const string Submit = "submit";
    public const string Cancel = "cancel";
    public const string CancelOperation = "cancel-operation";
    public const string Artifacts = "artifacts";
    public const string Retention = "retention";
    public const string Receipts = "receipts";
    public const string Invalid = "invalid-request";
    public const string NotFound = "not-found";
    public const string Stale = "selection-stale";
    public const string Conflict = "idempotency-conflict";
    public const string Expired = "artifact-expired";
    public const string Unauthorized = "unauthorized";
    public const string Unavailable = "temporarily-unavailable";
    public const string SourceId = "synthetic-publications";
}
/// <summary>Server identity and required executor profile with actual advertised limits.</summary>
public sealed record SystemInfo(string InstanceId, string ApiVersion, string ApplicationVersion, IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> OutputProfiles, int MaximumItems, long MaximumBytes, int DefaultRetentionDays, int OperationRetentionDays);
/// <summary>One configured URL-only source; search is deliberately absent.</summary>
public sealed record SourceInfo(string Id, string Name, IReadOnlyList<string> Operations, IReadOnlyList<string> MediaKinds, bool AuthenticationReady);
/// <summary>Bounded inspection request without execution effects.</summary>
public sealed record InspectRequest(string Url, string MediaKind, int MaxItems);
/// <summary>A finite selectable source item, independent of client entity types.</summary>
public sealed record SourceItem(string Id, string Title, string MediaKind, IReadOnlyList<string> Formats);
/// <summary>Durable, expiring selection evidence, revalidated only for new operations.</summary>
public sealed record Inspection(string Id, string Revision, DateTimeOffset ExpiresAt, string CanonicalUrl, string SourceId, IReadOnlyList<SourceItem> Items, IReadOnlyList<string> Warnings);
/// <summary>Source locator for a new job.</summary>
public sealed record JobInput(string Url);
/// <summary>Exact inspection and selected children.</summary>
public sealed record JobSelection(string Id, string Revision, IReadOnlyList<string> ItemIds);
/// <summary>Negotiated output representation; this profile produces one complete publication without sidecars.</summary>
public sealed record JobOutput(string Profile, string Format);
/// <summary>Enforced finite output budgets.</summary>
public sealed record JobLimits(int MaxItems, long MaxBytes);
/// <summary>Idempotent job submission. The body operation ID must match the Idempotency-Key header.</summary>
public sealed record SubmitJob(Guid ClientOperationId, JobInput Input, JobSelection Selection, JobOutput Output, JobLimits Limits);
/// <summary>Exact immutable artifact evidence, including an API-relative retrieval location.</summary>
public sealed record Artifact(string Id, string ItemId, string RelativePath, string MediaType, long SizeBytes, string Sha256, string Role, string ContentPath, string? GroupId = null, int? Ordinal = null);
/// <summary>Structured per-item terminal execution failure.</summary>
public sealed record ItemFailure(string ItemId, string Message);
/// <summary>Durable authoritative execution snapshot; artifact expiration never rewrites execution state.</summary>
public sealed record JobSnapshot(string InstanceId, string JobId, Guid ClientOperationId, long Revision, string State,
    double? Progress, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? ManifestRevision,
    DateTimeOffset RetainedUntil, bool ArtifactsExpired, DateTimeOffset? NextPollAfter, IReadOnlyList<ItemFailure> ItemFailures);
/// <summary>Durable operation fence with the original job when one was accepted before cancellation.</summary>
public sealed record OperationCancellation(string InstanceId, Guid ClientOperationId, bool PreventedAcceptance, JobSnapshot? Job);
/// <summary>One page of a sealed artifact set.</summary>
public sealed record ManifestPage(string JobId, string Revision, bool Sealed, int ArtifactCount, IReadOnlyList<Artifact> Artifacts, string? NextCursor);
/// <summary>Retention request. Granted leases cannot be revoked by cleanup.</summary>
public sealed record LeaseRequest(DateTimeOffset RetainUntil);
/// <summary>Guaranteed retention horizon.</summary>
public sealed record LeaseResult(string JobId, DateTimeOffset RetainedUntil);
/// <summary>Imported bytes and optional client-owned opaque reference.</summary>
public sealed record ImportedArtifact(string ArtifactId, string Sha256, string? ClientReference);
/// <summary>Idempotent acknowledgement of an already committed local import.</summary>
public sealed record ReceiptRequest(Guid ReceiptId, string ManifestRevision, IReadOnlyList<ImportedArtifact> Artifacts);
/// <summary>Exact receipt confirmation.</summary>
public sealed record ReceiptResult(string JobId, Guid ReceiptId, bool Accepted);
/// <summary>Stable HTTP failure without internal paths or secrets.</summary>
public sealed record ApiProblem(string Code, string Message, bool Retryable = false);
