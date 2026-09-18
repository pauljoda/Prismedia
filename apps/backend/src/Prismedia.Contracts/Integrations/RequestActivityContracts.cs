using System.Text.Json.Serialization;
using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Integrations;

/// <summary>One retained activity record with exactly one operation payload.</summary>
public sealed record RequestActivityItem(
    Guid Id,
    Guid ConnectionId,
    DateTimeOffset OccurredAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IntegrationTransferResponse? Transfer = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManagedRequestResponse? Request = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManagedTrackingResponse? Holding = null);

/// <summary>Last locally retained health observation for a connection represented in the activity view.</summary>
public sealed record RequestActivitySource(
    Guid ConnectionId,
    string Name,
    ConnectionStatus Status,
    bool IsStale,
    DateTimeOffset? LastCheckedAt,
    string? Problem);

/// <summary>
/// A deterministic keyset page of current locally retained activity. Callers restart at the first page
/// to observe records whose live status moved them between priority groups during pagination.
/// </summary>
public sealed record RequestActivityPage(
    IReadOnlyList<RequestActivityItem> Items,
    IReadOnlyList<RequestActivitySource> Sources,
    string? NextCursor,
    DateTimeOffset ReadAt);
