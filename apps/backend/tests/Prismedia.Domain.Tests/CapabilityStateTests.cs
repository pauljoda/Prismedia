using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Media;

namespace Prismedia.Domain.Tests;

public sealed class CapabilityStateTests {
    [Fact]
    public void FlagsPatchOnlyUpdatesProvidedValues() {
        var video = new Video(Guid.NewGuid(), "Test", subtitlesExtractedAt: null);
        video.PatchFlags(isFavorite: false, isNsfw: true, isOrganized: false);

        video.PatchFlags(isFavorite: true, isNsfw: null, isOrganized: null);

        Assert.True(video.IsFavorite);
        Assert.True(video.IsNsfw);
        Assert.False(video.IsOrganized);
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
