using Prismedia.Infrastructure.Videos;

namespace Prismedia.Infrastructure.Tests;

public sealed class MatroskaKeyframeReaderTests : IDisposable {
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pm-mkv-{Guid.NewGuid():N}.mkv");

    public void Dispose() {
        try {
            File.Delete(_path);
        } catch {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public void ReadsKeyframeTimesFromCuesIndexSkippingClusters() {
        // A minimal Matroska file: EBML header, then a Segment containing Info (TimecodeScale = 1ms),
        // a dummy Cluster (which the reader must seek PAST by its declared size), then Cues with three
        // CuePoints. CueTime is in TimecodeScale units (1ms here), so 0/6000/12000 -> 0/6/12 seconds.
        var info = Element(Id(0x15, 0x49, 0xA9, 0x66), Element(Id(0x2A, 0xD7, 0xB1), Uint(1_000_000)));
        var cluster = Element(Id(0x1F, 0x43, 0xB6, 0x75), new byte[256]); // large element to skip
        var cues = Element(
            Id(0x1C, 0x53, 0xBB, 0x6B),
            Concat(CuePoint(0), CuePoint(6000), CuePoint(12000)));
        var segment = Element(Id(0x18, 0x53, 0x80, 0x67), Concat(info, cluster, cues));
        var mkv = Concat(Element(Id(0x1A, 0x45, 0xDF, 0xA3), []), segment);
        File.WriteAllBytes(_path, mkv);

        var times = MatroskaKeyframeReader.TryReadKeyframeTimes(_path);

        Assert.NotNull(times);
        Assert.Equal([0.0, 6.0, 12.0], times);
    }

    [Fact]
    public void FiltersCueTimesToTheVideoTrackOnly() {
        // Cues index sync points for EVERY track. With Tracks declaring track 1 = video and track 2 =
        // audio, only the video track's Cue points (0/6/12s) are keyframes for the stream copy; the
        // audio Cue points (3/9s) must be ignored, or the VOD playlist would predict cuts ffmpeg's
        // video copy cannot make. This mirrors the real-file bug where unfiltered Cues over-counted 8x.
        var trackInfo = Element(Id(0x2A, 0xD7, 0xB1), Uint(1_000_000));
        var info = Element(Id(0x15, 0x49, 0xA9, 0x66), trackInfo);
        var tracks = Element(
            Id(0x16, 0x54, 0xAE, 0x6B),
            Concat(TrackEntry(number: 1, type: 1), TrackEntry(number: 2, type: 2)));
        var cues = Element(
            Id(0x1C, 0x53, 0xBB, 0x6B),
            Concat(
                CuePointForTrack(0, 1),
                CuePointForTrack(3000, 2),
                CuePointForTrack(6000, 1),
                CuePointForTrack(9000, 2),
                CuePointForTrack(12000, 1)));
        var segment = Element(Id(0x18, 0x53, 0x80, 0x67), Concat(info, tracks, cues));
        File.WriteAllBytes(_path, Concat(Element(Id(0x1A, 0x45, 0xDF, 0xA3), []), segment));

        var times = MatroskaKeyframeReader.TryReadKeyframeTimes(_path);

        Assert.Equal([0.0, 6.0, 12.0], times);
    }

    [Fact]
    public void ReturnsNullForNonMatroskaFile() {
        File.WriteAllBytes(_path, "not a matroska file"u8.ToArray());
        Assert.Null(MatroskaKeyframeReader.TryReadKeyframeTimes(_path));
    }

    [Fact]
    public void ReturnsNullWhenThereAreNoCues() {
        var info = Element(Id(0x15, 0x49, 0xA9, 0x66), Element(Id(0x2A, 0xD7, 0xB1), Uint(1_000_000)));
        var segment = Element(Id(0x18, 0x53, 0x80, 0x67), info);
        File.WriteAllBytes(_path, Concat(Element(Id(0x1A, 0x45, 0xDF, 0xA3), []), segment));

        Assert.Null(MatroskaKeyframeReader.TryReadKeyframeTimes(_path));
    }

    // --- minimal EBML writers ---

    private static byte[] CuePoint(long timeUnits) =>
        Element(Id(0xBB), Element(Id(0xB3), Uint(timeUnits)));

    private static byte[] CuePointForTrack(long timeUnits, long track) =>
        Element(
            Id(0xBB),
            Concat(
                Element(Id(0xB3), Uint(timeUnits)),
                Element(Id(0xB7), Element(Id(0xF7), Uint(track)))));

    private static byte[] TrackEntry(long number, long type) =>
        Element(
            Id(0xAE),
            Concat(Element(Id(0xD7), Uint(number)), Element(Id(0x83), Uint(type))));

    private static byte[] Id(params byte[] bytes) => bytes;

    private static byte[] Element(byte[] id, byte[] data) => Concat(id, SizeVint(data.Length), data);

    private static byte[] SizeVint(long n) {
        var len = 1;
        while (n >= (1L << (7 * len)) - 1) {
            len++;
        }

        var encoded = n | (1L << (7 * len));
        var bytes = new byte[len];
        for (var i = len - 1; i >= 0; i--) {
            bytes[i] = (byte)(encoded & 0xFF);
            encoded >>= 8;
        }

        return bytes;
    }

    private static byte[] Uint(long value) {
        if (value == 0) {
            return [0];
        }

        var bytes = new List<byte>();
        while (value > 0) {
            bytes.Insert(0, (byte)(value & 0xFF));
            value >>= 8;
        }

        return bytes.ToArray();
    }

    private static byte[] Concat(params byte[][] parts) {
        var result = new List<byte>();
        foreach (var part in parts) {
            result.AddRange(part);
        }

        return result.ToArray();
    }
}
