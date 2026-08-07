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
    string? Message = null,
    EntityDateType? ResolvedDateType = null,
    bool PreferChildAcquisitions = false) {
    /// <summary>An acquisition that is not release-gated.</summary>
    public static AcquisitionReleaseTimingDecision Ready { get; } = new(true);
}

/// <summary>Completeness and latest release milestone for one structural unit's direct children.</summary>
public sealed record EntityReleaseDateCoverage(
    int TotalChildren,
    int DatedChildren,
    EntityDate? LatestDate);

/// <summary>Reads one canonical release date from an Entity without exposing persistence to the use case.</summary>
public interface IEntityReleaseDateStore {
    Task<EntityDate?> GetAsync(Guid entityId, EntityDateType type, CancellationToken cancellationToken);

    /// <summary>Reads direct-child date coverage used to decide between whole-unit and per-child acquisition.</summary>
    Task<EntityReleaseDateCoverage> GetDirectChildCoverageAsync(
        Guid parentEntityId,
        EntityKind childKind,
        EntityDateType type,
        CancellationToken cancellationToken) =>
        Task.FromResult(new EntityReleaseDateCoverage(0, 0, LatestDate: null));
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

/// <summary>Refreshes waiting release gates when new date metadata satisfies them.</summary>
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

            await acquisitions.SetReleaseDateMetadataUnavailableAsync(
                detail.Summary.Id,
                unavailable: false,
                timing.Message,
                cancellationToken);

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
        if (kind == EntityKind.VideoSeason && entityId is { } seasonId) {
            var structuralDecision = await EvaluateVideoSeasonAsync(seasonId, cancellationToken);
            if (!structuralDecision.CanSearch) {
                return structuralDecision;
            }
        }

        var defaultType = DefaultAutomaticDateType(kind);
        var isImplicitEpisodeGate = policy.SearchAfterDateType is null && defaultType is not null;
        if ((policy.SearchAfterDateType ?? defaultType) is not { } type || entityId is not { } id) {
            return AcquisitionReleaseTimingDecision.Ready;
        }

        EntityDate? date = null;
        EntityDateType? resolvedType = null;
        foreach (var candidateType in ResolutionOrder(type)) {
            var candidate = await dates.GetAsync(id, candidateType, cancellationToken);
            if (candidate?.SortableValue is null) {
                continue;
            }

            date = candidate;
            resolvedType = candidateType;
            break;
        }
        if (date?.SortableValue is not { } sortable) {
            if (isImplicitEpisodeGate) {
                return new AcquisitionReleaseTimingDecision(
                    false,
                    type,
                    WaitingForMetadata: true,
                    Message: $"No {DisplayName(type)} date was included in the request metadata. Checking the configured provider before searching.");
            }
            return new AcquisitionReleaseTimingDecision(
                false,
                type,
                date,
                WaitingForMetadata: true,
                Message: $"No {DisplayName(type)} date was included in the request metadata. Checking the configured provider once before asking you to choose what to do.");
        }

        var milestone = EndOfPrecision(sortable, date.Precision);
        var searchNotBefore = milestone.AddDays(isImplicitEpisodeGate ? 0 : policy.SearchDelayDays);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return new AcquisitionReleaseTimingDecision(
            today >= searchNotBefore,
            type,
            date,
            searchNotBefore,
            Message: today >= searchNotBefore
                ? null
                : resolvedType == type
                    ? $"Waiting until {searchNotBefore:yyyy-MM-dd}, after the {DisplayName(type)} date."
                    : $"Waiting until {searchNotBefore:yyyy-MM-dd}, using the {DisplayName(resolvedType!.Value)} date for the {DisplayName(type)} preference.",
            ResolvedDateType: resolvedType);
    }

    /// <summary>
    /// A whole-season release cannot exist before its final episode. Incomplete or partially aired
    /// seasons therefore acquire their episodes independently: aired episodes can search immediately,
    /// future episodes wait on their own air dates, and the indexers never receive an impossible pack query.
    /// </summary>
    private async Task<AcquisitionReleaseTimingDecision> EvaluateVideoSeasonAsync(
        Guid seasonId,
        CancellationToken cancellationToken) {
        var coverage = await dates.GetDirectChildCoverageAsync(
            seasonId,
            EntityKind.VideoEpisode,
            EntityDateType.Air,
            cancellationToken);
        if (coverage.TotalChildren == 0
            || coverage.DatedChildren != coverage.TotalChildren
            || coverage.LatestDate?.SortableValue is null) {
            return new AcquisitionReleaseTimingDecision(
                false,
                EntityDateType.Air,
                coverage.LatestDate,
                WaitingForMetadata: true,
                Message: "Waiting for air dates for every episode before considering a whole-season search.",
                PreferChildAcquisitions: true);
        }

        var searchNotBefore = EndOfPrecision(
            coverage.LatestDate.SortableValue.Value,
            coverage.LatestDate.Precision);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (today >= searchNotBefore) {
            return AcquisitionReleaseTimingDecision.Ready;
        }

        return new AcquisitionReleaseTimingDecision(
            false,
            EntityDateType.Air,
            coverage.LatestDate,
            searchNotBefore,
            Message: $"Waiting until {searchNotBefore:yyyy-MM-dd}, when the final episode is scheduled to air.",
            ResolvedDateType: EntityDateType.Air,
            PreferChildAcquisitions: true);
    }

    /// <summary>
    /// Safety milestone applied even when a profile otherwise searches immediately. Episodes with a
    /// known future air date must not spend indexer or worker resources before they can exist.
    /// </summary>
    public static EntityDateType? DefaultAutomaticDateType(EntityKind kind) =>
        kind == EntityKind.VideoEpisode ? EntityDateType.Air : null;

    /// <summary>
    /// Ordered milestones that can satisfy a configured gate. Subscription streaming and digital/VOD
    /// describe the same downloadable availability tier across providers, so either may supply the date;
    /// an exact provider milestone wins when both exist.
    /// </summary>
    public static IReadOnlyList<EntityDateType> ResolutionOrder(EntityDateType configuredType) => configuredType switch {
        EntityDateType.StreamingRelease => [EntityDateType.StreamingRelease, EntityDateType.DigitalRelease],
        EntityDateType.DigitalRelease => [EntityDateType.DigitalRelease, EntityDateType.StreamingRelease],
        _ => [configuredType]
    };

    /// <summary>Whether a release milestone is meaningful for a profile kind.</summary>
    public static bool Supports(EntityKind kind, EntityDateType type) =>
        EntityKindRegistry.Describe(AcquisitionProfileKinds.For(kind))
            .AcquisitionProfile?
            .SupportedReleaseDateTypes
            .Contains(type) == true;

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
    /// request retains its waiting state and monitoring intent while offering date entry and manual search.
    /// </summary>
    public static string ReleaseDateUnavailableMessage(EntityDateType? type) => type is { } dateType
        ? $"The configured metadata provider did not return a {DisplayName(dateType)} date. This item is waiting: check again later, search manually, or enter the date yourself."
        : "The configured metadata provider did not return the required release date. This item is waiting: check again later, search manually, or enter the date yourself.";
}
