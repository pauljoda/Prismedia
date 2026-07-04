using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Requests;

/// <summary>
/// Covers <see cref="MonitorPresetSelection.Resolve"/> — the pure preset → selected-child-ids mapping, the
/// single source of truth for Sonarr's monitor-type-at-add semantics adapted to Prismedia's phantom model.
/// Each preset is asserted against a season list carrying ordering numbers and library-owned marks.
/// </summary>
public sealed class MonitorPresetSelectionTests {
    // A three-season list with a gap and a mid owned, plus one numberless work to exercise the extreme presets.
    private static IReadOnlyList<MonitorPresetCandidate> Seasons() => [
        new("p:S1", Number: 1, Owned: false),
        new("p:S2", Number: 2, Owned: true),
        new("p:S4", Number: 4, Owned: false),
        new("p:SExtra", Number: null, Owned: false),
    ];

    [Fact]
    public void AllSelectsEveryCandidateInInputOrder() {
        Assert.Equal(
            ["p:S1", "p:S2", "p:S4", "p:SExtra"],
            MonitorPresetSelection.Resolve(MonitorPreset.All, Seasons()));
    }

    [Fact]
    public void MissingSkipsOwnedCandidatesOnly() {
        Assert.Equal(
            ["p:S1", "p:S4", "p:SExtra"],
            MonitorPresetSelection.Resolve(MonitorPreset.Missing, Seasons()));
    }

    [Fact]
    public void FutureSelectsNothing() {
        Assert.Empty(MonitorPresetSelection.Resolve(MonitorPreset.Future, Seasons()));
    }

    [Fact]
    public void NoneSelectsNothing() {
        Assert.Empty(MonitorPresetSelection.Resolve(MonitorPreset.None, Seasons()));
    }

    [Fact]
    public void FirstSeasonSelectsTheSingleLowestNumberedCandidate() {
        Assert.Equal(["p:S1"], MonitorPresetSelection.Resolve(MonitorPreset.FirstSeason, Seasons()));
    }

    [Fact]
    public void LatestSeasonSelectsTheSingleHighestNumberedCandidate() {
        Assert.Equal(["p:S4"], MonitorPresetSelection.Resolve(MonitorPreset.LatestSeason, Seasons()));
    }

    [Fact]
    public void PilotResolvesToTheFirstSeasonAtThisGranularity() {
        // A single episode cannot be addressed at season-selection granularity, so Pilot == FirstSeason here
        // (the documented divergence). It must pick the lowest-numbered season, not merely the first in order.
        var outOfOrder = new MonitorPresetCandidate[] {
            new("p:S3", Number: 3, Owned: false),
            new("p:S1", Number: 1, Owned: false),
        };
        Assert.Equal(["p:S1"], MonitorPresetSelection.Resolve(MonitorPreset.Pilot, outOfOrder));
    }

    [Fact]
    public void ExtremePresetsIgnoreNumberlessCandidatesAndReturnEmptyWhenNoneCarryANumber() {
        var numberless = new MonitorPresetCandidate[] {
            new("p:A", Number: null, Owned: false),
            new("p:B", Number: null, Owned: false),
        };
        Assert.Empty(MonitorPresetSelection.Resolve(MonitorPreset.FirstSeason, numberless));
        Assert.Empty(MonitorPresetSelection.Resolve(MonitorPreset.LatestSeason, numberless));
    }

    [Fact]
    public void ExtremePresetsBreakTiesOnInputOrderSoADuplicateNumberNeverSelectsTwo() {
        var tie = new MonitorPresetCandidate[] {
            new("p:S1a", Number: 1, Owned: false),
            new("p:S1b", Number: 1, Owned: false),
        };
        Assert.Equal(["p:S1a"], MonitorPresetSelection.Resolve(MonitorPreset.FirstSeason, tie));
        Assert.Equal(["p:S1a"], MonitorPresetSelection.Resolve(MonitorPreset.LatestSeason, tie));
    }

    [Fact]
    public void EmptyCandidateListYieldsEmptyForEveryPreset() {
        foreach (var preset in Enum.GetValues<MonitorPreset>()) {
            Assert.Empty(MonitorPresetSelection.Resolve(preset, []));
        }
    }
}
