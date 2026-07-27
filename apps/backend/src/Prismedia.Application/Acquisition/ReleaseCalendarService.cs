using Prismedia.Contracts.Acquisition;

namespace Prismedia.Application.Acquisition;

/// <summary>Lists release and availability milestones across actively monitored requests.</summary>
public interface IReleaseCalendarService {
    Task<IReadOnlyList<ReleaseCalendarEvent>> ListAsync(
        DateOnly start,
        DateOnly end,
        bool hideNsfw,
        CancellationToken cancellationToken);
}

/// <summary>The release-oriented date types shown on the monitoring calendar.</summary>
public static class ReleaseCalendarDateTypes {
    public static readonly IReadOnlyList<Domain.Entities.EntityDateType> All = [
        Domain.Entities.EntityDateType.Announcement,
        Domain.Entities.EntityDateType.Premiere,
        Domain.Entities.EntityDateType.TheatricalRelease,
        Domain.Entities.EntityDateType.StreamingRelease,
        Domain.Entities.EntityDateType.DigitalRelease,
        Domain.Entities.EntityDateType.PhysicalRelease,
        Domain.Entities.EntityDateType.Air,
        Domain.Entities.EntityDateType.FirstAir,
        Domain.Entities.EntityDateType.LastAir,
        Domain.Entities.EntityDateType.Publication,
        Domain.Entities.EntityDateType.Release
    ];
}
