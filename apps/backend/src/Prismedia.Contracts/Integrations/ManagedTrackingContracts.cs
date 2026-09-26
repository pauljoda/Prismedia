using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Contracts.Integrations;

/// <summary>Retains explicit existing source associations; operation ID makes retries idempotent.</summary>
public sealed record TrackManagedHoldingRequest(Guid OperationId, Guid LibraryRootId, ManagedItemInput Item,
    IReadOnlyList<ManagedBindingSelection> Selections, bool CombineBookWorks = false);

/// <summary>Local owners suggested from exact file paths and numbering, with a reason when the complete scope cannot be linked.</summary>
public sealed record ManagedTrackingPreview(Guid? LibraryRootId, IReadOnlyList<ManagedBindingSelection> Selections,
    IReadOnlyList<ManagedLocalSource> Sources, string? ReviewReason);

/// <summary>Durable tracking health and exact associations; available files refer only to verified local bytes.</summary>
public sealed record ManagedTrackingResponse(Guid Id, Guid ConnectionId, Guid LibraryRootId, ManagedItemInput Item,
    string Title, ManagedTrackingStatus Status, long Revision, DateTimeOffset? LastCheckedAt, string? Problem,
    IReadOnlyList<ManagedFileBinding> Bindings, IReadOnlyList<ManagedTargetBinding> Targets,
    Guid? ReleaseOperationId = null, DateTimeOffset? ReleasedAt = null, Guid? BookWorkId = null);
