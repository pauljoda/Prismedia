using Prismedia.Domain.Capabilities;

namespace Prismedia.Domain.Tests;

public sealed class CapabilityStateTests {
    [Fact]
    public void FlagsPatchOnlyUpdatesProvidedValues() {
        var flags = new CapabilityFlags(isFavorite: false, isNsfw: true, isOrganized: false);

        flags.Patch(isFavorite: true, isNsfw: null, isOrganized: null);

        Assert.True(flags.IsFavorite);
        Assert.True(flags.IsNsfw);
        Assert.False(flags.IsOrganized);
    }

    [Fact]
    public void PlaybackUpdateAccumulatesDurationAndMarksCompletion() {
        var playback = new CapabilityPlayback();
        var completedAt = DateTimeOffset.Parse("2026-05-19T10:00:00Z");

        playback.Update(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(30), completed: true, completedAt);

        Assert.Equal(1, playback.Value.PlayCount);
        Assert.Equal(TimeSpan.FromSeconds(30), playback.Value.PlayDuration);
        Assert.Equal(TimeSpan.FromSeconds(12), playback.Value.ResumeTime);
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
