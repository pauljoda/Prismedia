using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>A downloader file-list observation, retained so later decisions can use current library facts.</summary>
public sealed record TvPayloadObservation(string Identity, DateTimeOffset ObservedAt, IReadOnlyList<ImportCandidateFile> Files);

/// <summary>Retains bounded, short-lived file-list evidence for a request and its linked Entity.</summary>
public interface ITvPayloadObservationStore {
    /// <summary>Stores evidence about the exact selected release without changing acquisition lifecycle state.</summary>
    Task RecordAsync(Guid acquisitionId, TvPayloadObservation observation, CancellationToken cancellationToken);
    /// <summary>Returns recent observations for the same request or Entity, newest observation per release.</summary>
    Task<IReadOnlyList<TvPayloadObservation>> ListAsync(AcquisitionSearchInput input, CancellationToken cancellationToken);
}

/// <summary>
/// Proves a season payload unnecessary only when every video maps to existing files and upgrades are off.
/// Unknown, missing, ambiguous, and potentially upgradeable content remains eligible.
/// </summary>
public sealed class TvPayloadAdmission(
    ITvPayloadObservationStore observations,
    IImportTargetIndex targets,
    IBookAcquisitionProfileStore profiles) {
    /// <summary>Human-readable explanation used by download recovery and queue validation.</summary>
    public const string NoBenefitMessage = "The season download only covers episodes already on disk, and this profile has upgrades disabled.";

    /// <summary>Checks the current mapping and actual owned paths; file sizes and release names alone never prove quality.</summary>
    public async Task<bool> HasNoBenefitAsync(AcquisitionSearchInput input, IReadOnlyList<ImportCandidateFile> files, CancellationToken cancellationToken) =>
        await LoadContextAsync(input, cancellationToken) is { } context && HasNoBenefit(context, files);

    private async Task<CoverageContext?> LoadContextAsync(AcquisitionSearchInput input, CancellationToken cancellationToken) {
        if (input.Kind != EntityKind.VideoSeason || input.EntityId is not { } entityId || input.SeasonNumber is not { } season
            || await profiles.GetAutoUpgradeAsync(input.ProfileId, input.Kind, cancellationToken)
            || await targets.HasUnnumberedWantedTvEpisodesAsync(entityId, season, cancellationToken)) {
            return null;
        }
        var titles = await targets.GetSeasonEpisodeTitlesAsync(entityId, season, cancellationToken);
        if (titles.Count == 0) return null;
        var layout = await targets.GetTvLayoutAsync(entityId, cancellationToken);
        if (layout is null || !Directory.Exists(layout.SeriesFolderPath)) return null;
        var owned = TvOwnedEpisodeCoverage.Read(layout);
        return new(input.WorkTitle, season, titles, owned);
    }

    private static bool HasNoBenefit(CoverageContext context, IReadOnlyList<ImportCandidateFile> files) {
        var plan = TvImportPlanBuilder.PlanUnits(files, context.Series, context.Season, null, episodeTitles: context.Titles);
        if (plan.Blocked || plan.Units.Count == 0) return false;
        var plannedFiles = plan.Units.Select(unit => unit.SourceRelativePath).ToHashSet(StringComparer.Ordinal);
        // Unmapped or out-of-season videos may contain something useful. An archive listing cannot
        // establish episode coverage either; wait until the downloader exposes actual video members.
        if (files.Any(file => !plannedFiles.Contains(file.RelativePath)
            && !(TvImportPlanBuilder.IsVideoFile(file.RelativePath) && MovieImportPlanBuilder.IsSampleFile(file.RelativePath))
            && !KnownCompanionExtensions.Contains(Path.GetExtension(file.RelativePath)))) return false;
        var known = context.Titles.Select(title => title.Episode).ToHashSet();
        foreach (var unit in plan.Units) {
            foreach (var episode in unit.ExtraEpisodes.Prepend(unit.Episode)) {
                if (!known.Contains(episode) || !context.Owned.Contains((unit.Season, episode))) return false;
            }
        }
        return true;
    }

    /// <summary>Records a proven non-beneficial payload before failed-download recovery removes its transfer.</summary>
    public Task RememberAsync(Guid acquisitionId, string identity, IReadOnlyList<ImportCandidateFile> files, CancellationToken cancellationToken) =>
        observations.RecordAsync(acquisitionId, new(identity, DateTimeOffset.UtcNow, files), cancellationToken);

    /// <summary>Reevaluates retained evidence; changing mappings, owned files, or upgrade policy reopens useful releases.</summary>
    public async Task<IReadOnlySet<string>> GetExcludedAsync(AcquisitionSearchInput input, CancellationToken cancellationToken) {
        if (input.Kind != EntityKind.VideoSeason) return new HashSet<string>(StringComparer.Ordinal);
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        var recorded = await observations.ListAsync(input, cancellationToken);
        if (recorded.Count == 0 || await LoadContextAsync(input, cancellationToken) is not { } context) return excluded;
        foreach (var observation in recorded) {
            if (HasNoBenefit(context, observation.Files)) excluded.Add(observation.Identity);
        }
        return excluded;
    }

    private sealed record CoverageContext(string Series, int Season, IReadOnlyList<TvEpisodeTitle> Titles, IReadOnlySet<(int, int)> Owned);

    // Any other unplanned file may conceal additional media (archives, obfuscated payloads, or future
    // formats). Only recognizable non-video companions can be ignored when proving no coverage gain.
    private static readonly IReadOnlySet<string> KnownCompanionExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".nfo", ".txt", ".jpg", ".jpeg", ".png", ".srt", ".ass", ".ssa", ".sub", ".idx", ".vtt", ".sfv", ".par2"
    };
}
