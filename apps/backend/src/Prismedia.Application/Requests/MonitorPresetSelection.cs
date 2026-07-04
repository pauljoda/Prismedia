using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// One candidate work a container commit can select at request time, reduced to just the fields the
/// preset mapping needs: its provider-qualified id (the selection token the commit consumes), its
/// season/volume ordering number when the provider gives one, and whether the library already owns it.
/// </summary>
/// <param name="QualifiedId">Provider-qualified id ("provider:workId") — the token returned in the selection.</param>
/// <param name="Number">
/// The work's ordering number (a season number, a volume number) when the provider declares one; null
/// otherwise. Drives <see cref="MonitorPreset.FirstSeason"/> / <see cref="MonitorPreset.LatestSeason"/>;
/// candidates with no number are treated as unordered and never chosen by those two presets.
/// </param>
/// <param name="Owned">True when the library already owns this work (it has a real file); a "missing" request skips it.</param>
public sealed record MonitorPresetCandidate(string QualifiedId, int? Number, bool Owned);

/// <summary>
/// The single source of truth mapping a <see cref="MonitorPreset"/> to the set of container children a
/// request commit selects — Sonarr's <c>MonitorTypes</c>-at-add-time semantics, adapted to Prismedia's
/// phantom model. Pure and side-effect-free so it is unit-tested in isolation; the commit service calls
/// it only when the UI sends a preset instead of an explicit child selection.
/// </summary>
public static class MonitorPresetSelection {
    /// <summary>
    /// The provider-qualified ids to request for <paramref name="preset"/> over <paramref name="candidates"/>.
    /// Order-preserving (candidates keep their input order in the result) and deterministic:
    /// <list type="bullet">
    /// <item><see cref="MonitorPreset.All"/> — every candidate.</item>
    /// <item><see cref="MonitorPreset.Future"/> — none now (the container watch handles future works).</item>
    /// <item><see cref="MonitorPreset.Missing"/> — every candidate the library does not already own.</item>
    /// <item><see cref="MonitorPreset.FirstSeason"/> — the single lowest-numbered candidate.</item>
    /// <item><see cref="MonitorPreset.LatestSeason"/> — the single highest-numbered candidate.</item>
    /// <item><see cref="MonitorPreset.Pilot"/> — the pilot: the first episode of the first season. At the
    /// season-selection granularity a commit works with, a single episode cannot be addressed, so this
    /// resolves to the first season (see the divergence note on <see cref="MonitorPreset.Pilot"/> handling
    /// in the commit service) — identical to <see cref="MonitorPreset.FirstSeason"/> here.</item>
    /// <item><see cref="MonitorPreset.None"/> — none.</item>
    /// </list>
    /// <see cref="MonitorPreset.FirstSeason"/>/<see cref="MonitorPreset.LatestSeason"/>/<see cref="MonitorPreset.Pilot"/>
    /// ignore candidates with no <see cref="MonitorPresetCandidate.Number"/>; when no candidate carries a
    /// number the result is empty. Ties on the extreme number resolve to the first such candidate in input
    /// order, so a repeated season number never selects two works.
    /// </summary>
    public static IReadOnlyList<string> Resolve(MonitorPreset preset, IReadOnlyList<MonitorPresetCandidate> candidates) =>
        preset switch {
            MonitorPreset.All => candidates.Select(candidate => candidate.QualifiedId).ToArray(),
            MonitorPreset.Missing => candidates.Where(candidate => !candidate.Owned).Select(candidate => candidate.QualifiedId).ToArray(),
            MonitorPreset.FirstSeason or MonitorPreset.Pilot => Extreme(candidates, lowest: true),
            MonitorPreset.LatestSeason => Extreme(candidates, lowest: false),
            // Future and None both request nothing up front; they differ only in the sync-time gate the
            // container monitor's persisted preset drives (Future auto-monitors new works, None does not).
            _ => []
        };

    /// <summary>The single candidate with the lowest (or highest) number, in input order for ties, as a one-element list; empty when no candidate carries a number.</summary>
    private static IReadOnlyList<string> Extreme(IReadOnlyList<MonitorPresetCandidate> candidates, bool lowest) {
        MonitorPresetCandidate? chosen = null;
        foreach (var candidate in candidates) {
            if (candidate.Number is not { } number) {
                continue;
            }

            if (chosen is not { Number: { } current }
                || (lowest ? number < current : number > current)) {
                chosen = candidate;
            }
        }

        return chosen is null ? [] : [chosen.QualifiedId];
    }
}
