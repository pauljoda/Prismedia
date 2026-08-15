namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>
/// Trigger-maintained availability snapshot for one Entity. Keeping derived state in a one-to-one
/// projection row lets background maintenance update it without changing the Entity's optimistic
/// concurrency token.
/// </summary>
public sealed class EntityAvailabilityRow {
    public Guid EntityId { get; set; }

    /// <summary>True when the Entity or a structural descendant owns a source file.</summary>
    public bool HasSourceMedia { get; set; }

    /// <summary>Latest acquisition status linked directly to the Entity, as a canonical code.</summary>
    public string? LatestAcquisitionStatusCode { get; set; }

    /// <summary>Canonical statuses represented by the Entity subtree and upgrade chains.</summary>
    public string[] AcquisitionStatusCodes { get; set; } = [];

    /// <summary>Last successful refresh of this persisted projection.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
