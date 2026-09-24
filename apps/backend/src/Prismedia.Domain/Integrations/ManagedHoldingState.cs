using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Persisted lifecycle of one connected holding, separate from its item identity and file associations.</summary>
public sealed record ManagedHoldingState(
    Guid Id,
    ManagedTrackingStatus Status,
    long Revision,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset NextCheckAt,
    string? Problem,
    Guid? ReleaseOperationId = null,
    DateTimeOffset? ReleasedAt = null);
