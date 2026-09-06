using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class TvMeasuredMergePlannerTests {
    [Theory]
    [InlineData(720, 1080, MergeFileAction.ReplaceUpgrade)]
    [InlineData(1080, 1080, MergeFileAction.DropNotUpgrade)]
    public async Task MeasuredResolutionCanUpgradeACleanFilenameButDoesNotInventSourceQuality(
        int ownedResolution, int candidateResolution, MergeFileAction expected) {
        var inspector = new Inspector(new(ownedResolution, candidateResolution, false, false, 1200, 1200));
        var result = await Plan(inspector);

        Assert.Null(result.HoldReason);
        Assert.Equal(expected, Assert.Single(result.Items).Action);
        Assert.Equal(1, inspector.Calls);
    }

    [Fact]
    public async Task AStale720pFilenameCannotMakeAnAlready1080pFileLookLikeAnUpgrade() {
        var result = await Plan(new Inspector(new(1080, 1080, false, false, 1200, 1200)), ownedName: "owned.720p.WEB-DL.mkv");

        Assert.Null(result.HoldReason);
        Assert.Equal(MergeFileAction.DropNotUpgrade, Assert.Single(result.Items).Action);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheCurrentProfileStillGatesAMeasuredUpgrade(bool languageMismatch) {
        var rules = BookAcquisitionRules.Default with {
            Kind = EntityKind.VideoSeason,
            AllowedQualities = languageMismatch ? [] : [VideoQuality.Webdl720p.ToCode()],
            PreferredLanguages = languageMismatch ? ["English"] : []
        };
        var result = await Plan(new Inspector(new(720, 1080, false, false, 1200, 1200, ["jpn"])), rules: rules);

        Assert.NotNull(result.HoldReason);
    }

    [Theory]
    [InlineData(false, MergeFileAction.DropFormatChange)]
    [InlineData(true, MergeFileAction.ReplaceUpgrade)]
    public async Task MeasuredUpgradeRetainsTheExplicitFormatChangeGate(bool allowFormatChange, MergeFileAction expected) {
        var result = await Plan(new Inspector(new(720, 1080, false, false, 1200, 1200)),
            ownedName: "owned.mp4", allowFormatChange: allowFormatChange);

        Assert.Null(result.HoldReason);
        Assert.Equal(expected, Assert.Single(result.Items).Action);
    }

    [Fact]
    public async Task AHighQualityPackMemberDoesNotElevateTheOtherFiles() {
        var units = new[] { Unit(1, "Show.S01E01.1080p.WEB-DL.mkv"), Unit(2, "Show.S01E02.720p.WEB-DL.mkv") };
        var inspector = new Inspector(new(720, 720, false, false, 1200, 1200)) {
            Resolve = path => new(720, path.Contains("1080p") ? 1080 : 720, false, false, 1200, 1200)
        };
        var result = await new TvMeasuredMergePlanner(inspector).PlanAsync(units,
            Layout(new() { [1] = "/library/Show/S01/owned1.720p.WEB-DL.mkv", [2] = "/library/Show/S01/owned2.720p.WEB-DL.mkv" }),
            SeasonSegment, new("/download", []), new("Show S01", null, null), Rules, false, default);

        Assert.Null(result.HoldReason);
        Assert.Equal([MergeFileAction.ReplaceUpgrade, MergeFileAction.DropNotUpgrade], result.Items.Select(item => item.Action));
        Assert.Equal(2, inspector.Calls);
    }

    [Fact]
    public async Task AMeasuredUpgradeCannotNarrowTheOwnersOfASharedFile() {
        var inspector = new Inspector(new(720, 1080, false, false, 1200, 1200));
        var result = await new TvMeasuredMergePlanner(inspector).PlanAsync([Unit(1)],
            Layout(new() { [1] = "/library/Show/S01/shared.mkv", [2] = "/library/Show/S01/shared.mkv" }),
            SeasonSegment, new("/download", []), null, Rules, false, default);

        Assert.Null(result.HoldReason);
        Assert.Equal(MergeFileAction.HoldStructuralConflict, Assert.Single(result.Items).Action);
    }

    [Fact]
    public async Task NewEpisodesAndConflictingPhysicalCoverageDoNotTriggerUnnecessaryProbes() {
        var inspector = new Inspector(null);
        var combined = Unit(1) with { ExtraEpisodes = [2] };
        var result = await new TvMeasuredMergePlanner(inspector).PlanAsync([combined, Unit(3)],
            Layout(new() { [1] = "/library/Show/S01/first.mkv", [2] = "/library/Show/S01/second.mkv" }),
            SeasonSegment, new("/download", []), null, Rules, false, default);

        Assert.Null(result.HoldReason);
        Assert.Equal([MergeFileAction.HoldStructuralConflict, MergeFileAction.PlaceNew], result.Items.Select(item => item.Action));
        Assert.Equal(0, inspector.Calls);
    }

    [Fact]
    public async Task AnHonestlyLabeledLowerQualityFileRemainsANonUpgradeWithoutHoldingThePack() {
        var inspector = new Inspector(new(1080, 720, false, false, 1200, 1200));
        var result = await new TvMeasuredMergePlanner(inspector).PlanAsync([Unit(1, "Show.S01E01.720p.WEB-DL.mkv")],
            Layout(new() { [1] = "/library/Show/S01/owned.1080p.WEB-DL.mkv" }),
            SeasonSegment, new("/download", []), null, Rules, false, default);

        Assert.Null(result.HoldReason);
        Assert.Equal(MergeFileAction.DropNotUpgrade, Assert.Single(result.Items).Action);
    }

    [Fact]
    public async Task IdenticalBytesReconcileWithoutProbingEvenWhenTheNewNameClaimsAHigherQuality() {
        var root = Directory.CreateTempSubdirectory("prismedia-measured-identical-").FullName;
        try {
            var owned = Path.Combine(root, "owned.720p.WEB-DL.mkv");
            var candidate = Path.Combine(root, "Show.S01E01.1080p.WEB-DL.mkv");
            await File.WriteAllTextAsync(owned, "same episode bytes");
            File.Copy(owned, candidate);
            var inspector = new Inspector(null);

            var result = await new TvMeasuredMergePlanner(inspector).PlanAsync([Unit(1, Path.GetFileName(candidate))],
                Layout(new() { [1] = owned }), SeasonSegment, new(root, []), null, Rules, false, default);

            Assert.Null(result.HoldReason);
            Assert.Contains(Path.GetFileName(candidate), result.MatchingExisting);
            Assert.Equal(MergeFileAction.DropNotUpgrade, Assert.Single(result.Items).Action);
            Assert.Equal(0, inspector.Calls);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationStopsPlanningWithoutTurningItIntoAReviewHold() {
        var inspector = new Inspector(null) { Resolve = _ => throw new OperationCanceledException() };
        await Assert.ThrowsAsync<OperationCanceledException>(() => Plan(inspector));
    }

    private static BookAcquisitionRules Rules => BookAcquisitionRules.Default with { Kind = EntityKind.VideoSeason };
    private static string SeasonSegment(int season) => $"Season {season:00}";
    private static TvPlanUnit Unit(int episode, string? source = null) => new(
        source ?? $"Show.S01E{episode:00}.1080p.WEB-DL.mkv", 1, episode, $"Show/Season 01/Show.S01E{episode:00}.mkv");
    private static TvSeriesDiskLayout Layout(Dictionary<int, string> files) => new(Guid.NewGuid(), "/library/Show",
        new Dictionary<int, TvSeasonDiskLayout> { [1] = new(Guid.NewGuid(), "/library/Show/S01", files) });
    private static Task<TvMeasuredMergePlan> Plan(Inspector inspector, string ownedName = "owned.mkv",
        BookAcquisitionRules? rules = null, bool allowFormatChange = false) =>
        new TvMeasuredMergePlanner(inspector).PlanAsync([Unit(1)], Layout(new() { [1] = "/library/Show/S01/" + ownedName }),
            SeasonSegment, new("/download", []), null, rules ?? Rules, allowFormatChange, default);

    private sealed class Inspector(MediaUpgradePayloadInspection? result) : IMediaUpgradePayloadInspector {
        public int Calls { get; private set; }
        public Func<string, MediaUpgradePayloadInspection?>? Resolve { get; init; }
        public Task<MediaUpgradePayloadInspection?> InspectAsync(string ownedContentPath, string candidateContentPath,
            CancellationToken cancellationToken) {
            Calls++;
            return Task.FromResult(Resolve is null ? result : Resolve(candidateContentPath));
        }
    }
}
