using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
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
    public void RecordCompletedIncrementsCompletionCountOnlyOnTransition() {
        var consumption = new CapabilityConsumption();
        var at = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

        consumption.RecordCompleted(at);
        consumption.RecordCompleted(at.AddSeconds(1));

        // Repeated completion signals within one watched state are idempotent.
        Assert.Equal(1, consumption.Value.CompletionCount);
        Assert.Equal(TimeSpan.Zero, consumption.Value.ResumeTime);
        Assert.NotNull(consumption.Value.CompletedAt);
    }

    [Fact]
    public void RecordCompletedOccurrenceIncrementsAlreadyCompletedItems() {
        var consumption = new CapabilityConsumption();
        var at = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

        consumption.RecordCompletedOccurrence(at);
        consumption.RecordCompletedOccurrence(at.AddMinutes(4));

        Assert.Equal(2, consumption.Value.CompletionCount);
        Assert.Equal(TimeSpan.Zero, consumption.Value.ResumeTime);
        Assert.Equal(at.AddMinutes(4), consumption.Value.CompletedAt);
    }

    [Fact]
    public void RecordStartOverReArmsCompletionForAnotherCount() {
        var playback = new CapabilityConsumption();
        var at = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

        playback.RecordCompleted(at);
        playback.RecordStartOver(at.AddMinutes(1));
        playback.RecordCompleted(at.AddMinutes(2));

        Assert.Equal(2, playback.Value.CompletionCount);
        Assert.Equal(at.AddMinutes(2), playback.Value.CompletedAt);
    }

    [Fact]
    public void RecordResumeLeavesCountAndCompletionUntouched() {
        var playback = new CapabilityConsumption();
        var at = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

        playback.RecordCompleted(at);
        playback.RecordResume(TimeSpan.FromSeconds(42), at.AddMinutes(1));

        // A mid-watch resume report after completion stores the position without
        // clearing the watched state or advancing the completion count.
        Assert.Equal(1, playback.Value.CompletionCount);
        Assert.Equal(TimeSpan.FromSeconds(42), playback.Value.ResumeTime);
        Assert.NotNull(playback.Value.CompletedAt);
    }

    [Fact]
    public void MarkCompletedAndIncompleteToggleStateIndependentlyOfResume() {
        var consumption = new CapabilityConsumption();
        var at = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

        consumption.RecordResume(TimeSpan.FromSeconds(42), at);
        consumption.MarkCompleted(at.AddSeconds(1));

        Assert.Equal(1, consumption.Value.CompletionCount);
        Assert.Equal(TimeSpan.FromSeconds(42), consumption.Value.ResumeTime);
        Assert.NotNull(consumption.Value.CompletedAt);

        consumption.MarkIncomplete(at.AddSeconds(2));

        Assert.Equal(1, consumption.Value.CompletionCount);
        Assert.Equal(TimeSpan.FromSeconds(42), consumption.Value.ResumeTime);
        Assert.Null(consumption.Value.CompletedAt);
    }

    [Fact]
    public void PlaybackAccumulatesDurationAndClearsResumeOnCompletion() {
        var playback = new CapabilityConsumption();
        var completedAt = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

        playback.RecordResume(TimeSpan.FromSeconds(12), completedAt.AddMinutes(-1));
        playback.AccumulateActiveDuration(TimeSpan.FromSeconds(30), completedAt.AddSeconds(-1));
        playback.RecordCompleted(completedAt);

        Assert.Equal(1, playback.Value.CompletionCount);
        Assert.Equal(TimeSpan.FromSeconds(30), playback.Value.ActiveDuration);
        Assert.Equal(TimeSpan.Zero, playback.Value.ResumeTime);
        Assert.Equal(completedAt, playback.Value.CompletedAt);
    }

    [Fact]
    public void RecordSkippedIncrementsSkipCountWithoutChangingCompletion() {
        var playback = new CapabilityConsumption();
        var completedAt = DateTimeOffset.Parse("2026-05-19T10:00:00Z");
        var skippedAt = completedAt.AddMinutes(5);

        playback.RecordCompleted(completedAt);
        playback.RecordSkipped(skippedAt);

        Assert.Equal(1, playback.Value.CompletionCount);
        Assert.Equal(1, playback.Value.SkipCount);
        Assert.Equal(completedAt, playback.Value.CompletedAt);
        Assert.Equal(skippedAt, playback.Value.LastActiveAt);
    }

    [Fact]
    public void HistoricalCompletedAndSkippedEventsIncreaseCountersWithoutRegressingNewerPlaybackState() {
        var playback = new CapabilityConsumption();
        var historicalAt = DateTimeOffset.Parse("2026-05-19T10:00:00Z");
        var newerAt = historicalAt.AddMinutes(5);
        playback.RecordResume(TimeSpan.FromSeconds(120), newerAt);

        playback.RecordCompletedOccurrence(historicalAt);
        playback.RecordSkipped(historicalAt.AddMinutes(1));

        Assert.Equal(1, playback.Value.CompletionCount);
        Assert.Equal(1, playback.Value.SkipCount);
        Assert.Equal(TimeSpan.FromSeconds(120), playback.Value.ResumeTime);
        Assert.Equal(newerAt, playback.Value.LastActiveAt);
        Assert.Null(playback.Value.CompletedAt);
    }

    [Fact]
    public void RecordAccessedTracksEachOpenAndKeepsNewestTimestamp() {
        var consumption = new CapabilityConsumption();
        var latest = DateTimeOffset.Parse("2026-05-19T10:05:00Z");

        consumption.RecordAccessed(latest);
        consumption.RecordAccessed(latest.AddMinutes(-5));

        Assert.Equal(2, consumption.Value.AccessCount);
        Assert.Equal(latest, consumption.Value.LastAccessedAt);
        Assert.Equal(latest, consumption.Value.LastActiveAt);
    }

    [Fact]
    public void HistoricalProgressResetAndUnreadSignalsDoNotRegressNewerProgress() {
        var newerAt = DateTimeOffset.Parse("2026-05-19T10:05:00Z");
        var progress = new CapabilityProgress(
            currentEntityId: Guid.NewGuid(),
            unit: ProgressUnit.Page,
            index: 4,
            total: 10,
            mode: ReaderMode.Paged,
            completedAt: newerAt,
            updatedAt: newerAt);

        Assert.False(progress.TryMoveTo(
            Guid.NewGuid(),
            ProgressUnit.Page,
            index: 0,
            total: 10,
            mode: ReaderMode.Paged,
            updatedAt: newerAt.AddMinutes(-1)));
        Assert.False(progress.TryMarkIncomplete(newerAt.AddMinutes(-1)));
        Assert.Equal(4, progress.Index);
        Assert.Equal(newerAt, progress.UpdatedAt);
        Assert.Equal(newerAt, progress.CompletedAt);
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
