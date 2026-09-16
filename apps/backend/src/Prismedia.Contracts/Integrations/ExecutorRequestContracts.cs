using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Integrations;

/// <summary>Read-only inspection of a user-selected source URL through a connected executor.</summary>
public sealed record InspectExecutorRequest(string Url, EntityKind EntityKind);

/// <summary>Bounded choices whose source locator and remote selection revision remain inside a protected host token.</summary>
public sealed record ExecutorInspectionResponse(string SelectionToken, DateTimeOffset ExpiresAt,
    IReadOnlyList<InspectedTransferItem> Items, IReadOnlyList<string> Warnings);

/// <summary>Idempotent acceptance of one inspected publication into an explicit local library.</summary>
public sealed record AcquireExecutorItemRequest(Guid OperationId, string SelectionToken, string ItemId, Guid LibraryRootId);
