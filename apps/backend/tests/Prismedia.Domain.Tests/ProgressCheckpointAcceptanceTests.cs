using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Tests;

/// <summary>
/// Guards the per-modality checkpoint rules that let reading and listening keep independent exact
/// positions: an older signal loses only to its own modality, and completion decides resumability.
/// </summary>
public sealed class ProgressCheckpointAcceptanceTests {
    private static readonly Guid BookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TrackId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-24T12:00:00Z");

    [Fact]
    public void CheckpointsAcceptPerModalityAndResumeRelativeToCompletion() {
        var progress = new CapabilityProgress();
        var reading = Reading(index: 2_300, at: Start);
        var listening = Listening(offset: 112.5, at: Start.AddMinutes(1));

        Assert.True(progress.TryRecord(reading));
        Assert.True(progress.TryRecord(listening));

        // An older reading signal loses; the newer listening checkpoint is untouched.
        Assert.False(progress.TryRecord(Reading(index: 1_000, at: Start.AddSeconds(-30))));
        Assert.Same(reading, progress.CheckpointFor(ConsumptionModality.Reading));
        Assert.Same(listening, progress.CheckpointFor(ConsumptionModality.Listening));

        // An equal timestamp is accepted, like the main cursor's latest-signal rule.
        var sameInstant = Reading(index: 2_400, at: Start);
        Assert.True(progress.TryRecord(sameInstant));
        Assert.Same(sameInstant, progress.CheckpointFor(ConsumptionModality.Reading));
        Assert.Equal(ConsumptionModality.Listening, progress.LastModality);
        Assert.Same(listening, progress.Resumable);

        // Equal newest timestamps resolve to reading.
        Assert.True(progress.TryRecord(Reading(index: 2_500, at: Start.AddMinutes(1))));
        Assert.Equal(ConsumptionModality.Reading, progress.LastModality);

        // The completing signal shares its checkpoint's timestamp, so nothing is resumable.
        var completedAt = Start.AddMinutes(2);
        var finalListening = Listening(offset: 900, at: completedAt);
        Assert.True(progress.TryRecord(finalListening));
        Assert.True(progress.TryMoveTo(BookId, ProgressUnit.Second, 900, 900, null, completedAt, completed: true));
        Assert.Equal(ConsumptionModality.Listening, progress.LastModality);
        Assert.Null(progress.Resumable);
        Assert.False(progress.IsResumable(progress.CheckpointFor(ConsumptionModality.Reading)!));

        // A later signal makes its own modality resumable again without clearing completion.
        var reread = Reading(index: 100, at: completedAt.AddMinutes(5));
        Assert.True(progress.TryRecord(reread));
        Assert.Equal(completedAt, progress.CompletedAt);
        Assert.Same(reread, progress.Resumable);
        Assert.False(progress.IsResumable(finalListening));
    }

    [Theory]
    [InlineData(ProgressUnit.Cfi, 10, 9_999, "whole-work total")]
    [InlineData(ProgressUnit.Second, 10, 100, "cannot be recorded")]
    [InlineData(ProgressUnit.Page, 12, 10, "between 0 and its total")]
    public void ReadingCheckpointsRejectPositionsOutsideTheirModality(
        ProgressUnit unit,
        int index,
        int total,
        string expectedMessage) {
        var error = Assert.Throws<ArgumentException>(() =>
            ConsumptionModalityDefinition.Reading.Checkpoint(BookId, unit, index, total, Start));

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ListeningCheckpointsDeriveTheirIndexFromAFiniteOffset() {
        var checkpoint = Listening(offset: 417.25, at: Start);

        Assert.Equal(417, checkpoint.Index);
        Assert.Equal(900, checkpoint.Total);
        Assert.Equal(ProgressUnit.Second, checkpoint.Unit);
        Assert.Throws<ArgumentException>(() =>
            ConsumptionModalityDefinition.Listening.OffsetCheckpoint(TrackId, null, double.NaN, 900, Start));
        Assert.Throws<ArgumentException>(() =>
            ConsumptionModalityDefinition.Listening.Checkpoint(
                TrackId, ProgressUnit.Second, 10, 900, Start, offsetSeconds: 10, mode: ReaderMode.Paged));
    }

    private static ProgressCheckpoint Reading(int index, DateTimeOffset at) =>
        ConsumptionModalityDefinition.Reading.Checkpoint(
            BookId,
            ProgressUnit.Cfi,
            index,
            ConsumptionModalityDefinition.ReadablePositionTotal,
            at,
            mode: ReaderMode.Paged,
            location: "epubcfi(/6/12!/4/2)");

    private static ProgressCheckpoint Listening(double offset, DateTimeOffset at) =>
        ConsumptionModalityDefinition.Listening.OffsetCheckpoint(TrackId, null, offset, 900, at);
}
