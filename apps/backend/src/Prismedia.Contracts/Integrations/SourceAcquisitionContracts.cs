using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Integrations;

/// <summary>Requests one pinned publication after the host has durably accepted its operation. Replays must coalesce the same source selection.</summary>
public sealed record RequestSourceInput(Guid OperationId, SourceSelection Selection, string OfferId);

/// <summary>Reads readiness of the exact source selection without enqueuing downloads, updating reading progress, or changing its scope.</summary>
public sealed record ObserveSourceInput(SourceSelection Selection, string OfferId);

/// <summary>Source-owned preparation evidence. Ready permits resolving a direct offer; it does not prove retained bytes or a committed local import.</summary>
public sealed record SourceAcquisitionObservation(SourceSelection Selection, string OfferId,
    CatalogPublication Publication, CatalogOffer Offer, SourceAcquisitionState State,
    double? Progress = null, DateTimeOffset? NextPollAfter = null, string? Problem = null);
