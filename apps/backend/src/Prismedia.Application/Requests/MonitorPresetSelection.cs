using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>
/// One candidate work a container commit can select at request time, reduced to just the fields the
/// preset mapping needs: its opaque selection token (a legacy provider-qualified id or a canonical
/// proposal id), its
/// and whether the library already owns it.
/// </summary>
/// <param name="SelectionId">Opaque token returned in the selection.</param>
/// <param name="Owned">True when the library already owns this work (it has a real file); a "missing" request skips it.</param>
public sealed record MonitorPresetCandidate(string SelectionId, bool Owned);

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
    /// <item><see cref="MonitorPreset.None"/> — none.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> Resolve(MonitorPreset preset, IReadOnlyList<MonitorPresetCandidate> candidates) =>
        preset switch {
            MonitorPreset.All => candidates.Select(candidate => candidate.SelectionId).ToArray(),
            MonitorPreset.Missing => candidates.Where(candidate => !candidate.Owned).Select(candidate => candidate.SelectionId).ToArray(),
            // Future and None both request nothing up front; they differ only in the sync-time gate the
            // container monitor's persisted preset drives (Future auto-monitors new works, None does not).
            _ => []
        };
}
