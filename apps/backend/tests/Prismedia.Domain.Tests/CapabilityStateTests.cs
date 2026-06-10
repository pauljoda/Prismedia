using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Media;

namespace Prismedia.Domain.Tests;

public sealed class CapabilityStateTests {
    [Fact]
    public void FlagsPatchOnlyUpdatesProvidedValues() {
        var video = new Video(Guid.NewGuid(), "Test");
        video.PatchFlags(isFavorite: false, isNsfw: true, isOrganized: false);

        video.PatchFlags(isFavorite: true, isNsfw: null, isOrganized: null);

        Assert.True(video.IsFavorite);
        Assert.True(video.IsNsfw);
        Assert.False(video.IsOrganized);
    }

    [Fact]
    public void RecordCompletedIncrementsPlayCountOnlyOnTransition() {
        var playback = new CapabilityPlayback();
        var at = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

        playback.RecordCompleted(at);
        playback.RecordCompleted(at.AddSeconds(1));

        // Repeated completion signals within one watched state are idempotent.
        Assert.Equal(1, playback.Value.PlayCount);
        Assert.Equal(TimeSpan.Zero, playback.Value.ResumeTime);
        Assert.NotNull(playback.Value.CompletedAt);
    }

    [Fact]
    public void RecordCompletedPlayIncrementsAlreadyCompletedItems() {
        var playback = new CapabilityPlayback();
        var at = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

        playback.RecordCompletedPlay(at);
        playback.RecordCompletedPlay(at.AddMinutes(4));

        Assert.Equal(2, playback.Value.PlayCount);
        Assert.Equal(TimeSpan.Zero, playback.Value.ResumeTime);
        Assert.Equal(at.AddMinutes(4), playback.Value.CompletedAt);
    }

    [Fact]
    public void RecordStartOverReArmsCompletionForAnotherCount() {
        var playback = new CapabilityPlayback();
        var at = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

        playback.RecordCompleted(at);
        playback.RecordStartOver(at.AddMinutes(1));
        playback.RecordCompleted(at.AddMinutes(2));

        Assert.Equal(2, playback.Value.PlayCount);
        Assert.Equal(at.AddMinutes(2), playback.Value.CompletedAt);
    }

    [Fact]
    public void RecordResumeLeavesCountAndCompletionUntouched() {
        var playback = new CapabilityPlayback();
        var at = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

        playback.RecordCompleted(at);
        playback.RecordResume(TimeSpan.FromSeconds(42), at.AddMinutes(1));

        // A mid-watch resume report after completion stores the position without
        // clearing the watched state or advancing the play count.
        Assert.Equal(1, playback.Value.PlayCount);
        Assert.Equal(TimeSpan.FromSeconds(42), playback.Value.ResumeTime);
        Assert.NotNull(playback.Value.CompletedAt);
    }

    [Fact]
    public void MarkWatchedAndUnwatchedTogglesCompletionIndependentlyOfResume() {
        var playback = new CapabilityPlayback();
        var at = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

        playback.RecordResume(TimeSpan.FromSeconds(42), at);
        playback.MarkWatched(at.AddSeconds(1));

        // Marking watched sets completion and counts the play without disturbing the resume point.
        Assert.Equal(1, playback.Value.PlayCount);
        Assert.Equal(TimeSpan.FromSeconds(42), playback.Value.ResumeTime);
        Assert.NotNull(playback.Value.CompletedAt);

        playback.MarkUnwatched(at.AddSeconds(2));

        // Marking unwatched clears completion, again leaving the resume point and count intact.
        Assert.Equal(1, playback.Value.PlayCount);
        Assert.Equal(TimeSpan.FromSeconds(42), playback.Value.ResumeTime);
        Assert.Null(playback.Value.CompletedAt);
    }

    [Fact]
    public void PlaybackAccumulatesDurationAndClearsResumeOnCompletion() {
        var playback = new CapabilityPlayback();
        var completedAt = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

        playback.RecordResume(TimeSpan.FromSeconds(12), completedAt.AddMinutes(-1));
        playback.AccumulatePlayDuration(TimeSpan.FromSeconds(30));
        playback.RecordCompleted(completedAt);

        Assert.Equal(1, playback.Value.PlayCount);
        Assert.Equal(TimeSpan.FromSeconds(30), playback.Value.PlayDuration);
        Assert.Equal(TimeSpan.Zero, playback.Value.ResumeTime);
        Assert.Equal(completedAt, playback.Value.CompletedAt);
    }

    [Fact]
    public void MarkersAddUpdateAndDeleteByIdentifier() {
        var markers = new CapabilityMarkers();

        var id = markers.Add(" intro ", 5, 10);
        var updated = markers.Update(id, "Scene", 12, 14);
        var deleted = markers.Delete(id);

        Assert.True(updated);
        Assert.True(deleted);
        Assert.Empty(markers.Items);
    }
}
