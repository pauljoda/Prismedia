using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Requests;

/// <summary>
/// Covers <see cref="MonitorPresetSelection.Resolve"/> — the pure preset → selected-child-ids mapping, the
/// single source of truth for generic container monitoring defaults. Medium-specific selection remains
/// explicit in the request review rather than leaking season semantics into the Entity core.
/// </summary>
public sealed class MonitorPresetSelectionTests {
    private static IReadOnlyList<MonitorPresetCandidate> Works() => [
        new("p:A", Owned: false),
        new("p:B", Owned: true),
        new("p:C", Owned: false),
    ];

    [Fact]
    public void AllSelectsEveryCandidateInInputOrder() {
        Assert.Equal(
            ["p:A", "p:B", "p:C"],
            MonitorPresetSelection.Resolve(MonitorPreset.All, Works()));
    }

    [Fact]
    public void MissingSkipsOwnedCandidatesOnly() {
        Assert.Equal(
            ["p:A", "p:C"],
            MonitorPresetSelection.Resolve(MonitorPreset.Missing, Works()));
    }

    [Fact]
    public void FutureSelectsNothing() {
        Assert.Empty(MonitorPresetSelection.Resolve(MonitorPreset.Future, Works()));
    }

    [Fact]
    public void NoneSelectsNothing() {
        Assert.Empty(MonitorPresetSelection.Resolve(MonitorPreset.None, Works()));
    }

    [Fact]
    public void EmptyCandidateListYieldsEmptyForEveryPreset() {
        foreach (var preset in Enum.GetValues<MonitorPreset>()) {
            Assert.Empty(MonitorPresetSelection.Resolve(preset, []));
        }
    }
}
