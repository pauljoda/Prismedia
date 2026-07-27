using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Acquisition;

/// <summary>
/// One semantic release milestone for an actively monitored request. Calendar consumers can distinguish
/// informational dates from the profile-selected search gate and show when automatic searching unlocks.
/// </summary>
public sealed record ReleaseCalendarEvent(
    Guid EntityId,
    Guid MonitorId,
    Guid? AcquisitionId,
    EntityKind Kind,
    string Title,
    Guid? ParentEntityId,
    EntityKind? ParentKind,
    string? ParentTitle,
    EntityDateType DateType,
    string Value,
    DateOnly Date,
    DatePrecision Precision,
    AcquisitionStatus? AcquisitionStatus,
    bool IsSearchGate,
    DateOnly? SearchNotBefore,
    bool? IsSearchEligible,
    string? PosterUrl = null);
