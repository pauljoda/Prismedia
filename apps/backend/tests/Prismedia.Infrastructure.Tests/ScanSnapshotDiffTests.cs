using Prismedia.Application.Jobs.Scanning;

namespace Prismedia.Infrastructure.Tests;

public sealed class ScanSnapshotDiffTests {
    private static FileSignature Sig(string path, long size = 100, long ticks = 1) =>
        new(path, size, ticks);

    [Fact]
    public void IdenticalSnapshotsProduceNoChanges() {
        var previous = new[] { Sig("/a.mkv"), Sig("/b.mkv"), Sig("/c.mkv") };
        var current = new[] { Sig("/a.mkv"), Sig("/b.mkv"), Sig("/c.mkv") };

        var delta = ScanSnapshotDiff.Compute(previous, current);

        Assert.False(delta.HasChanges);
        Assert.Empty(delta.Added);
        Assert.Empty(delta.Removed);
        Assert.Empty(delta.Changed);
        Assert.Equal(3, delta.UnchangedCount);
    }

    [Fact]
    public void EnumerationOrderDoesNotAffectClassification() {
        // A positional (line) diff would mark everything after an insertion as changed; the set/map
        // comparison must be order-independent. Reverse the current order and insert one new file.
        var previous = new[] { Sig("/a.mkv"), Sig("/b.mkv"), Sig("/c.mkv") };
        var current = new[] { Sig("/c.mkv"), Sig("/b.mkv"), Sig("/a.mkv"), Sig("/aa-new.mkv") };

        var delta = ScanSnapshotDiff.Compute(previous, current);

        Assert.Equal(["/aa-new.mkv"], delta.Added.Select(s => s.Path).ToArray());
        Assert.Empty(delta.Removed);
        Assert.Empty(delta.Changed);
        Assert.Equal(3, delta.UnchangedCount);
    }

    [Fact]
    public void DetectsAddedAndRemovedPaths() {
        var previous = new[] { Sig("/keep.mkv"), Sig("/gone.mkv") };
        var current = new[] { Sig("/keep.mkv"), Sig("/fresh.mkv") };

        var delta = ScanSnapshotDiff.Compute(previous, current);

        Assert.Equal(["/fresh.mkv"], delta.Added.Select(s => s.Path).ToArray());
        Assert.Equal(["/gone.mkv"], delta.Removed.Select(s => s.Path).ToArray());
        Assert.Empty(delta.Changed);
        Assert.Equal(1, delta.UnchangedCount);
        Assert.True(delta.HasChanges);
    }

    [Fact]
    public void DetectsInPlaceModificationBySize() {
        var previous = new[] { Sig("/movie.mkv", size: 100, ticks: 5) };
        var current = new[] { Sig("/movie.mkv", size: 200, ticks: 5) };

        var delta = ScanSnapshotDiff.Compute(previous, current);

        var changed = Assert.Single(delta.Changed);
        Assert.Equal("/movie.mkv", changed.Path);
        Assert.Equal(200, changed.SizeBytes);
        Assert.Empty(delta.Added);
        Assert.Empty(delta.Removed);
        Assert.Equal(0, delta.UnchangedCount);
    }

    [Fact]
    public void DetectsInPlaceModificationByModifiedTime() {
        var previous = new[] { Sig("/movie.mkv", size: 100, ticks: 5) };
        var current = new[] { Sig("/movie.mkv", size: 100, ticks: 9) };

        var delta = ScanSnapshotDiff.Compute(previous, current);

        Assert.Equal("/movie.mkv", Assert.Single(delta.Changed).Path);
    }

    [Fact]
    public void WindowsPathComparisonIsCaseInsensitive() {
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        var previous = new[] { Sig("/Movies/Film.mkv") };
        var current = new[] { Sig("/movies/film.mkv") };

        var delta = ScanSnapshotDiff.Compute(previous, current);

        Assert.False(delta.HasChanges);
        Assert.Equal(1, delta.UnchangedCount);
    }

    [Fact]
    public void UnixCaseDistinctPathsAreAddedAndRemoved() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        var previous = new[] { Sig("/Movies/Film.mkv") };
        var current = new[] { Sig("/movies/film.mkv") };

        var delta = ScanSnapshotDiff.Compute(previous, current);

        Assert.Equal("/movies/film.mkv", Assert.Single(delta.Added).Path);
        Assert.Equal("/Movies/Film.mkv", Assert.Single(delta.Removed).Path);
        Assert.Empty(delta.Changed);
        Assert.Equal(0, delta.UnchangedCount);
    }

    [Fact]
    public void EmptyPreviousMarksEverythingAdded() {
        var current = new[] { Sig("/a.mkv"), Sig("/b.mkv") };

        var delta = ScanSnapshotDiff.Compute([], current);

        Assert.Equal(2, delta.Added.Count);
        Assert.Empty(delta.Removed);
        Assert.Empty(delta.Changed);
    }

    [Fact]
    public void EmptyCurrentMarksEverythingRemoved() {
        var previous = new[] { Sig("/a.mkv"), Sig("/b.mkv") };

        var delta = ScanSnapshotDiff.Compute(previous, []);

        Assert.Equal(2, delta.Removed.Count);
        Assert.Empty(delta.Added);
        Assert.Empty(delta.Changed);
    }

    [Fact]
    public void BothEmptyHasNoChanges() {
        var delta = ScanSnapshotDiff.Compute([], []);

        Assert.False(delta.HasChanges);
        Assert.Same(ScanDelta.Empty.Added, ScanDelta.Empty.Added);
        Assert.Equal(0, delta.UnchangedCount);
    }
}
