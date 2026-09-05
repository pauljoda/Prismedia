using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class VideoQualityDetectionTests {
    [Theory]
    [InlineData("Film.2000.DVD9.720p.HDDVD.x264", VideoQuality.Bluray720p)]
    [InlineData("Film.2000.720p.HD-DVD.x264", VideoQuality.Bluray720p)]
    [InlineData("Film.2000.1080p.HD.DVD.x264", VideoQuality.Bluray1080p)]
    [InlineData("Film.2000.1080p.HDDVDRip.x264", VideoQuality.Bluray1080p)]
    [InlineData("Film.2000.1080p.HD-DVDRip.x264", VideoQuality.Bluray1080p)]
    [InlineData("Film.2000.1080p.HDDVD.REMUX", VideoQuality.Remux1080p)]
    [InlineData("Film.2000.DVD9", VideoQuality.Dvd)]
    [InlineData("Film.2000.DVDRip.XviD", VideoQuality.Dvd)]
    public void HighDefinitionDiscSourcesUseTheHdDiscLadder(string release, VideoQuality expected) {
        Assert.Equal(expected, VideoQualityDetection.Detect(release));
    }
}
