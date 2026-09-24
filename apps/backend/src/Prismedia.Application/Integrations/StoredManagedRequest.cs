using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Request journal independent of transient jobs and manager queue history.</summary>
public sealed record StoredManagedRequest(ManagedRequestOperation Operation, ManagedRequestPlan Plan,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Problem);
