using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Projects typed Entity dates, active monitors, acquisitions, and profile gates into calendar events.</summary>
public sealed class EfReleaseCalendarService(PrismediaDbContext db, TimeProvider timeProvider)
    : IReleaseCalendarService {
    public async Task<IReadOnlyList<ReleaseCalendarEvent>> ListAsync(
        DateOnly start,
        DateOnly end,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var targets = await (
            from monitor in db.Monitors.AsNoTracking()
            where monitor.Status == MonitorStatus.Active
            join acquisition in db.Acquisitions.AsNoTracking()
                on monitor.AcquisitionId equals acquisition.Id into joinedAcquisitions
            from acquisition in joinedAcquisitions.DefaultIfEmpty()
            let entityId = monitor.EntityId ?? (acquisition == null ? null : acquisition.EntityId)
            where entityId != null
            select new CalendarTarget(
                monitor.Id,
                entityId!.Value,
                monitor.AcquisitionId,
                monitor.Kind,
                monitor.Title,
                monitor.ProfileId ?? (acquisition == null ? null : acquisition.ProfileId),
                acquisition == null ? null : acquisition.Status,
                acquisition == null ? null : acquisition.PosterUrl))
            .ToArrayAsync(cancellationToken);
        if (targets.Length == 0) {
            return [];
        }

        var entityIds = targets.Select(target => target.EntityId).Distinct().ToArray();
        var entities = await db.Entities.AsNoTracking()
            .Where(entity => entityIds.Contains(entity.Id) && (!hideNsfw || !entity.IsNsfw))
            .Select(entity => new CalendarEntity(
                entity.Id,
                entity.KindCode,
                entity.Title,
                entity.ParentEntityId))
            .ToDictionaryAsync(entity => entity.Id, cancellationToken);
        if (entities.Count == 0) {
            return [];
        }

        var parentIds = entities.Values
            .Where(entity => entity.ParentEntityId != null)
            .Select(entity => entity.ParentEntityId!.Value)
            .Distinct()
            .ToArray();
        var parents = parentIds.Length == 0
            ? new Dictionary<Guid, CalendarParent>()
            : await db.Entities.AsNoTracking()
                .Where(entity => parentIds.Contains(entity.Id) && (!hideNsfw || !entity.IsNsfw))
                .Select(entity => new CalendarParent(entity.Id, entity.KindCode, entity.Title, entity.ParentEntityId))
                .ToDictionaryAsync(entity => entity.Id, cancellationToken);

        var grandparentIds = parents.Values
            .Where(parent => parent.ParentEntityId != null)
            .Select(parent => parent.ParentEntityId!.Value)
            .Distinct()
            .ToArray();
        var grandparents = grandparentIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Entities.AsNoTracking()
                .Where(entity => grandparentIds.Contains(entity.Id) && (!hideNsfw || !entity.IsNsfw))
                .ToDictionaryAsync(entity => entity.Id, entity => entity.Title, cancellationToken);

        var visibleIds = entities.Keys.ToArray();
        var dateCodes = ReleaseCalendarDateTypes.All.Select(type => type.ToCode()).ToArray();
        var dates = await db.EntityDates.AsNoTracking()
            .Where(date => visibleIds.Contains(date.EntityId)
                && dateCodes.Contains(date.Code)
                && date.SortableValue != null)
            .OrderBy(date => date.SortableValue)
            .ThenBy(date => date.Code)
            .ToArrayAsync(cancellationToken);

        var profiles = await db.BookAcquisitionProfiles.AsNoTracking().ToArrayAsync(cancellationToken);
        var targetByEntity = targets
            .Where(target => entities.ContainsKey(target.EntityId))
            .GroupBy(target => target.EntityId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(target => target.AcquisitionStatus == AcquisitionStatus.WaitingForRelease)
                    .ThenBy(target => target.MonitorId)
                    .First());
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var result = new List<ReleaseCalendarEvent>(dates.Length);
        foreach (var date in dates) {
            if (date.SortableValue < start || date.SortableValue > end) {
                continue;
            }
            if (!targetByEntity.TryGetValue(date.EntityId, out var target)
                || !date.Code.TryDecodeAs<EntityDateType>(out var dateType)) {
                continue;
            }

            var profileKind = AcquisitionProfileKinds.For(target.Kind);
            var profile = target.ProfileId is { } profileId
                ? profiles.FirstOrDefault(candidate => candidate.Id == profileId && candidate.Kind == profileKind)
                : null;
            profile ??= profiles
                .Where(candidate => candidate.Kind == profileKind)
                .OrderByDescending(candidate => candidate.IsDefault)
                .ThenBy(candidate => candidate.CreatedAt)
                .FirstOrDefault();
            var configuredGate = profile?.SearchAfterDateType
                ?? AcquisitionReleaseTimingService.DefaultAutomaticDateType(target.Kind);
            var resolvedGateType = configuredGate is { } gate
                ? AcquisitionReleaseTimingService.ResolutionOrder(gate)
                    .Where(candidateType => dates.Any(candidate =>
                        candidate.EntityId == date.EntityId
                        && candidate.Code == candidateType.ToCode()
                        && candidate.SortableValue != null))
                    .Select(candidateType => (EntityDateType?)candidateType)
                    .FirstOrDefault()
                : (EntityDateType?)null;
            var isSearchGate = resolvedGateType == dateType;
            var precision = date.Precision is { } precisionCode
                && precisionCode.TryDecodeAs<DatePrecision>(out var decodedPrecision)
                    ? decodedPrecision
                    : DatePrecision.Day;
            var searchNotBefore = isSearchGate
                ? AcquisitionReleaseTimingService.EndOfPrecision(date.SortableValue!.Value, date.Precision)
                    .AddDays(profile?.SearchAfterDateType is null ? 0 : profile.SearchDelayDays)
                : (DateOnly?)null;
            var calendarEntity = entities[target.EntityId];
            var parent = calendarEntity.ParentEntityId is { } parentId
                && parents.TryGetValue(parentId, out var resolvedParent)
                    ? resolvedParent
                    : null;
            var parentKind = parent?.KindCode.TryDecodeAs<EntityKind>(out var decodedParentKind) == true
                ? decodedParentKind
                : (EntityKind?)null;
            var grandparentTitle = parent?.ParentEntityId is { } grandparentId
                ? grandparents.GetValueOrDefault(grandparentId)
                : null;
            result.Add(new ReleaseCalendarEvent(
                target.EntityId,
                target.MonitorId,
                target.AcquisitionId,
                target.Kind,
                calendarEntity.Title,
                parent?.Id,
                parentKind,
                parent?.Title,
                grandparentTitle,
                dateType,
                date.Value,
                date.SortableValue!.Value,
                precision,
                target.AcquisitionStatus,
                isSearchGate,
                searchNotBefore,
                isSearchGate ? today >= searchNotBefore : null,
                target.PosterUrl));
        }

        return result;
    }

    private sealed record CalendarTarget(
        Guid MonitorId,
        Guid EntityId,
        Guid? AcquisitionId,
        EntityKind Kind,
        string Title,
        Guid? ProfileId,
        AcquisitionStatus? AcquisitionStatus,
        string? PosterUrl);

    private sealed record CalendarEntity(Guid Id, string KindCode, string Title, Guid? ParentEntityId);

    private sealed record CalendarParent(Guid Id, string KindCode, string Title, Guid? ParentEntityId);
}
