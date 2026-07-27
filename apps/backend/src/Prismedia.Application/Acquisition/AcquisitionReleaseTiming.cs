using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>The release milestone and optional grace period that gate automatic searches for a profile.</summary>
public sealed record AcquisitionReleaseTimingPolicy(EntityDateType? SearchAfterDateType, int SearchDelayDays) {
    /// <summary>A profile with no automatic release gate.</summary>
    public static AcquisitionReleaseTimingPolicy Immediate { get; } = new(null, 0);
}

/// <summary>The current search eligibility of one acquisition under its resolved profile.</summary>
public sealed record AcquisitionReleaseTimingDecision(
    bool CanSearch,
    EntityDateType? DateType = null,
    EntityDate? Date = null,
    DateOnly? SearchNotBefore = null,
    bool WaitingForMetadata = false,
    string? Message = null) {
    /// <summary>An acquisition that is not release-gated.</summary>
    public static AcquisitionReleaseTimingDecision Ready { get; } = new(true);
}

/// <summary>Reads one canonical release date from an Entity without exposing persistence to the use case.</summary>
public interface IEntityReleaseDateStore {
    Task<EntityDate?> GetAsync(Guid entityId, EntityDateType type, CancellationToken cancellationToken);
}

/// <summary>Evaluates whether automatic searching may begin for one requested Entity.</summary>
public interface IAcquisitionReleaseTimingService {
    Task<AcquisitionReleaseTimingDecision> EvaluateAsync(
        Guid? entityId,
        Guid? profileId,
        EntityKind kind,
        CancellationToken cancellationToken);
}

/// <summary>
/// Re-evaluates release-gated acquisitions after typed Entity dates change. Metadata apply paths call
/// this after releasing their Entity mutation lease so a provider or user-supplied milestone can resume
/// waiting work without a second request.
/// </summary>
public interface IAcquisitionReleaseDateChangeHandler {
    Task HandleAsync(Guid entityId, CancellationToken cancellationToken);
}

/// <summary>Moves manual release holds back into automatic timing when new date metadata satisfies them.</summary>
public sealed class AcquisitionReleaseDateChangeHandler(
    IAcquisitionStore acquisitions,
    IAcquisitionReleaseTimingService releaseTiming,
    IMonitorStore monitors) : IAcquisitionReleaseDateChangeHandler {
    public async Task HandleAsync(Guid entityId, CancellationToken cancellationToken) {
        var details = await acquisitions.ListForEntityAsync(entityId, cancellationToken);
        foreach (var detail in details.Where(detail => detail.Summary.Status is
                     AcquisitionStatus.WaitingForRelease or AcquisitionStatus.ManualSearchRequired)) {
            var import = await acquisitions.GetImportContextAsync(detail.Summary.Id, cancellationToken);
            if (import is null) {
                continue;
            }

            var timing = await releaseTiming.EvaluateAsync(
                entityId,
                import.ProfileId,
                import.Kind,
                cancellationToken);
            if (timing.WaitingForMetadata) {
                continue;
            }

            if (detail.Summary.Status == AcquisitionStatus.ManualSearchRequired) {
                var moved = await acquisitions.TryTransitionStatusAsync(
                    detail.Summary.Id,
                    [AcquisitionStatus.ManualSearchRequired],
                    AcquisitionStatus.WaitingForRelease,
                    timing.Message,
                    cancellationToken);
                if (!moved) {
                    continue;
                }
            }

            if (timing.CanSearch) {
                await monitors.MarkSearchDueByAcquisitionAsync(detail.Summary.Id, cancellationToken);
            }
        }
    }
}

/// <summary>
/// Applies a profile's selected release milestone to the typed dates supplied by metadata providers.
/// Manual release searches intentionally bypass this service; it gates automatic request and monitor work.
/// </summary>
public sealed class AcquisitionReleaseTimingService(
    IBookAcquisitionProfileStore profiles,
    IEntityReleaseDateStore dates,
    TimeProvider timeProvider) : IAcquisitionReleaseTimingService {
    public async Task<AcquisitionReleaseTimingDecision> EvaluateAsync(
        Guid? entityId,
        Guid? profileId,
        EntityKind kind,
        CancellationToken cancellationToken) {
        var policy = await profiles.GetReleaseTimingAsync(profileId, kind, cancellationToken);
        if (policy.SearchAfterDateType is not { } type || entityId is not { } id) {
            return AcquisitionReleaseTimingDecision.Ready;
        }

        var date = await dates.GetAsync(id, type, cancellationToken);
        if (date?.SortableValue is not { } sortable) {
            return new AcquisitionReleaseTimingDecision(
                false,
                type,
                date,
                WaitingForMetadata: true,
                Message: $"No {DisplayName(type)} date was included in the request metadata. Checking the configured provider once before asking you to choose what to do.");
        }

        var milestone = EndOfPrecision(sortable, date.Precision);
        var searchNotBefore = milestone.AddDays(policy.SearchDelayDays);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return new AcquisitionReleaseTimingDecision(
            today >= searchNotBefore,
            type,
            date,
            searchNotBefore,
            Message: today >= searchNotBefore
                ? null
                : $"Waiting until {searchNotBefore:yyyy-MM-dd}, after the {DisplayName(type)} date.");
    }

    /// <summary>Whether a release milestone is meaningful for a profile kind.</summary>
    public static bool Supports(EntityKind kind, EntityDateType type) => AcquisitionProfileKinds.For(kind) switch {
        EntityKind.Movie => type is EntityDateType.Premiere
            or EntityDateType.TheatricalRelease
            or EntityDateType.StreamingRelease
            or EntityDateType.DigitalRelease
            or EntityDateType.PhysicalRelease
            or EntityDateType.Release,
        EntityKind.VideoSeries => type is EntityDateType.Premiere
            or EntityDateType.Air
            or EntityDateType.FirstAir
            or EntityDateType.StreamingRelease
            or EntityDateType.DigitalRelease
            or EntityDateType.Release,
        EntityKind.Book => type is EntityDateType.Publication
            or EntityDateType.DigitalRelease
            or EntityDateType.PhysicalRelease
            or EntityDateType.Release,
        EntityKind.AudioLibrary => type is EntityDateType.Release
            or EntityDateType.DigitalRelease
            or EntityDateType.PhysicalRelease,
        _ => false
    };

    /// <summary>Last date represented by an imprecise year or month, preventing an early automatic search.</summary>
    public static DateOnly EndOfPrecision(DateOnly value, string? precision) {
        if (precision == DatePrecision.Year.ToCode()) {
            return new DateOnly(value.Year, 12, 31);
        }

        if (precision == DatePrecision.Month.ToCode()) {
            return new DateOnly(value.Year, value.Month, DateTime.DaysInMonth(value.Year, value.Month));
        }

        return value;
    }

    /// <summary>User-facing name used in lifecycle status messages.</summary>
    public static string DisplayName(EntityDateType type) => type switch {
        EntityDateType.Announcement => "announcement",
        EntityDateType.Premiere => "premiere",
        EntityDateType.TheatricalRelease => "theatrical release",
        EntityDateType.StreamingRelease => "streaming release",
        EntityDateType.DigitalRelease => "digital release",
        EntityDateType.PhysicalRelease => "physical release",
        EntityDateType.Air => "air",
        EntityDateType.FirstAir => "first air",
        EntityDateType.LastAir => "last air",
        EntityDateType.Publication => "publication",
        EntityDateType.Release => "release",
        EntityDateType.Birth => "birth",
        EntityDateType.Death => "death",
        EntityDateType.CareerStart => "career start",
        EntityDateType.CareerEnd => "career end",
        _ => type.ToCode()
    };

    /// <summary>
    /// Explains that a configured release gate could not be evaluated after its metadata refresh. The
    /// request retains its monitoring intent, but only an explicit manual search may bypass the unavailable milestone.
    /// </summary>
    public static string ManualSearchRequiredMessage(EntityDateType? type) => type is { } dateType
        ? $"The configured metadata provider did not return a {DisplayName(dateType)} date. This item is waiting: check again later, search manually, or enter the date yourself."
        : "The configured metadata provider did not return the required release date. This item is waiting: check again later, search manually, or enter the date yourself.";
}
