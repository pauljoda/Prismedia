namespace Prismedia.Contracts.Entities;

/// <summary>API-facing timeline, page, or navigation marker.</summary>
/// <param name="Id">Stable marker identifier.</param>
/// <param name="Title">Human-readable marker label.</param>
/// <param name="Seconds">Start time in seconds from the beginning of the media.</param>
/// <param name="EndSeconds">Optional end time in seconds when the marker spans a range.</param>
public sealed record EntityMarker(Guid Id, string Title, double Seconds, double? EndSeconds);

/// <summary>API-facing marker capability.</summary>
[CapabilityKind("markers")]
public sealed record MarkersCapability(IReadOnlyList<EntityMarker> Items) : EntityCapability;
