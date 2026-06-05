using Microsoft.Extensions.Logging;

namespace Prismedia.Infrastructure.Videos;

/// <summary>
/// Reads keyframe timestamps from a Matroska/WebM file's <c>Cues</c> index without scanning the whole
/// file. Matroska stores a Cues element — a seek index of keyframe positions and times — near the end
/// of the file; reading just that index is near-instant, whereas a full ffprobe packet walk of a large
/// 4K source reads the entire file (tens of seconds on the box: ~22s for a 25 GB movie). This mirrors
/// how reference media servers build a seekable HLS playlist from the container index rather than a
/// heavy probe, and keeps the remux manifest fast without any scan-time work.
/// </summary>
/// <remarks>
/// The reader walks the Segment's top-level children, seeking past large elements (Clusters) by their
/// declared size so it only ever reads element headers plus the small Info and Cues elements. It
/// returns <c>null</c> on anything it cannot parse (not Matroska, unknown-size elements, missing Cues),
/// so the caller falls back to the ffprobe scan and correctness is never at risk.
/// </remarks>
internal static class MatroskaKeyframeReader {
    // Matroska EBML element IDs, read with their length-marker bits preserved (as stored on disk).
    private const long IdEbmlHeader = 0x1A45DFA3;
    private const long IdSegment = 0x18538067;
    private const long IdInfo = 0x1549A966;
    private const long IdTimecodeScale = 0x2AD7B1;
    private const long IdTracks = 0x1654AE6B;
    private const long IdTrackEntry = 0xAE;
    private const long IdTrackNumber = 0xD7;
    private const long IdTrackType = 0x83;
    private const long VideoTrackType = 1;
    private const long IdCues = 0x1C53BB6B;
    private const long IdCuePoint = 0xBB;
    private const long IdCueTime = 0xB3;
    private const long IdCueTrackPositions = 0xB7;
    private const long IdCueTrack = 0xF7;

    // Matroska's default timecode scale: timestamps are expressed in this many nanoseconds.
    private const long DefaultTimecodeScaleNs = 1_000_000;

    /// <summary>
    /// Attempts to read sorted keyframe presentation times (in seconds) from the file's Cues index.
    /// Returns null when the file is not Matroska, has no usable Cues, or cannot be parsed.
    /// </summary>
    /// <param name="path">Absolute path to the source file.</param>
    /// <param name="logger">Optional logger for parse diagnostics.</param>
    public static IReadOnlyList<double>? TryReadKeyframeTimes(string path, ILogger? logger = null) {
        try {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);

            if (ReadElementId(stream) != IdEbmlHeader) {
                return null;
            }

            var headerSize = ReadElementSize(stream);
            if (headerSize < 0) {
                return null;
            }

            stream.Seek(headerSize, SeekOrigin.Current);

            if (ReadElementId(stream) != IdSegment) {
                return null;
            }

            var segmentSize = ReadElementSize(stream);
            var segmentStart = stream.Position;
            var segmentEnd = segmentSize >= 0 ? segmentStart + segmentSize : stream.Length;

            double timecodeScaleNs = DefaultTimecodeScaleNs;
            long? videoTrack = null;
            List<long>? cueTimes = null;

            // Walk the Segment's top-level children. Info and Tracks come before Cues, so by the time we
            // reach Cues we know the timecode scale and the video track number to filter Cue points by
            // (the Cues index every track's sync points; we only want the video track's keyframes).
            var guard = 0;
            while (stream.Position < segmentEnd && guard++ < 1_000_000) {
                var id = ReadElementId(stream);
                if (id < 0) {
                    break;
                }

                var size = ReadElementSize(stream);
                if (size < 0) {
                    // Unknown-size or truncated element we would need to skip — give up cleanly.
                    break;
                }

                var dataStart = stream.Position;
                if (id == IdInfo) {
                    timecodeScaleNs = ReadTimecodeScale(stream, dataStart, size) ?? DefaultTimecodeScaleNs;
                } else if (id == IdTracks) {
                    videoTrack = ReadVideoTrackNumber(stream, dataStart, size);
                } else if (id == IdCues) {
                    cueTimes = ReadCueTimes(stream, dataStart, size, videoTrack);
                    break; // Cues is the only thing left we need; it comes last in the Segment.
                }

                stream.Seek(dataStart + size, SeekOrigin.Begin);
            }

            if (cueTimes is null || cueTimes.Count == 0) {
                return null;
            }

            var seconds = cueTimes
                .Select(time => time * timecodeScaleNs / 1_000_000_000.0)
                .Where(time => time >= 0 && double.IsFinite(time))
                .Distinct()
                .OrderBy(time => time)
                .ToList();

            return seconds.Count > 0 ? seconds : null;
        } catch (Exception ex) {
            logger?.LogDebug(ex, "Matroska Cues read failed for {Path}; falling back to ffprobe.", path);
            return null;
        }
    }

    private static double? ReadTimecodeScale(Stream stream, long dataStart, long size) {
        var end = dataStart + size;
        stream.Seek(dataStart, SeekOrigin.Begin);
        while (stream.Position < end) {
            var id = ReadElementId(stream);
            if (id < 0) {
                break;
            }

            var elementSize = ReadElementSize(stream);
            if (elementSize < 0) {
                break;
            }

            var childStart = stream.Position;
            if (id == IdTimecodeScale) {
                return ReadUInt(stream, elementSize);
            }

            stream.Seek(childStart + elementSize, SeekOrigin.Begin);
        }

        return null;
    }

    // Finds the video track's TrackNumber so Cue points can be filtered to it.
    private static long? ReadVideoTrackNumber(Stream stream, long dataStart, long size) {
        var end = dataStart + size;
        stream.Seek(dataStart, SeekOrigin.Begin);
        while (stream.Position < end) {
            var id = ReadElementId(stream);
            if (id < 0) {
                break;
            }

            var elementSize = ReadElementSize(stream);
            if (elementSize < 0) {
                break;
            }

            var childStart = stream.Position;
            if (id == IdTrackEntry) {
                var (number, type) = ReadTrackEntry(stream, childStart, elementSize);
                if (type == VideoTrackType && number.HasValue) {
                    return number;
                }
            }

            stream.Seek(childStart + elementSize, SeekOrigin.Begin);
        }

        return null;
    }

    private static (long? Number, long? Type) ReadTrackEntry(Stream stream, long dataStart, long size) {
        long? number = null;
        long? type = null;
        var end = dataStart + size;
        stream.Seek(dataStart, SeekOrigin.Begin);
        while (stream.Position < end) {
            var id = ReadElementId(stream);
            if (id < 0) {
                break;
            }

            var elementSize = ReadElementSize(stream);
            if (elementSize < 0) {
                break;
            }

            var childStart = stream.Position;
            if (id == IdTrackNumber) {
                number = (long)ReadUInt(stream, elementSize);
            } else if (id == IdTrackType) {
                type = (long)ReadUInt(stream, elementSize);
            }

            stream.Seek(childStart + elementSize, SeekOrigin.Begin);
        }

        return (number, type);
    }

    // Reads the CueTime of each CuePoint that indexes the video track. When the video track number is
    // unknown (no Tracks element parsed), all Cue points are kept rather than dropping everything.
    private static List<long> ReadCueTimes(Stream stream, long dataStart, long size, long? videoTrack) {
        var times = new List<long>();
        var end = dataStart + size;
        stream.Seek(dataStart, SeekOrigin.Begin);
        while (stream.Position < end) {
            var id = ReadElementId(stream);
            if (id < 0) {
                break;
            }

            var elementSize = ReadElementSize(stream);
            if (elementSize < 0) {
                break;
            }

            var childStart = stream.Position;
            if (id == IdCuePoint) {
                var (time, track) = ReadCuePoint(stream, childStart, elementSize);
                if (time.HasValue && (videoTrack is null || track == videoTrack)) {
                    times.Add(time.Value);
                }
            }

            stream.Seek(childStart + elementSize, SeekOrigin.Begin);
        }

        return times;
    }

    private static (long? Time, long? Track) ReadCuePoint(Stream stream, long dataStart, long size) {
        long? time = null;
        long? track = null;
        var end = dataStart + size;
        stream.Seek(dataStart, SeekOrigin.Begin);
        while (stream.Position < end) {
            var id = ReadElementId(stream);
            if (id < 0) {
                break;
            }

            var elementSize = ReadElementSize(stream);
            if (elementSize < 0) {
                break;
            }

            var childStart = stream.Position;
            if (id == IdCueTime) {
                time = (long)ReadUInt(stream, elementSize);
            } else if (id == IdCueTrackPositions) {
                track = ReadCueTrack(stream, childStart, elementSize) ?? track;
            }

            stream.Seek(childStart + elementSize, SeekOrigin.Begin);
        }

        return (time, track);
    }

    private static long? ReadCueTrack(Stream stream, long dataStart, long size) {
        var end = dataStart + size;
        stream.Seek(dataStart, SeekOrigin.Begin);
        while (stream.Position < end) {
            var id = ReadElementId(stream);
            if (id < 0) {
                break;
            }

            var elementSize = ReadElementSize(stream);
            if (elementSize < 0) {
                break;
            }

            var childStart = stream.Position;
            if (id == IdCueTrack) {
                return (long)ReadUInt(stream, elementSize);
            }

            stream.Seek(childStart + elementSize, SeekOrigin.Begin);
        }

        return null;
    }

    private static double ReadUInt(Stream stream, long size) {
        ulong value = 0;
        for (long i = 0; i < size && i < 8; i++) {
            var b = stream.ReadByte();
            if (b < 0) {
                break;
            }

            value = (value << 8) | (byte)b;
        }

        return value;
    }

    // Reads a Matroska element ID (1-4 bytes, marker bits preserved). Returns -1 at EOF / on error.
    private static long ReadElementId(Stream stream) {
        var first = stream.ReadByte();
        if (first < 0) {
            return -1;
        }

        var length = LeadingLength(first);
        if (length is < 1 or > 4) {
            return -1;
        }

        long id = first;
        for (var i = 1; i < length; i++) {
            var b = stream.ReadByte();
            if (b < 0) {
                return -1;
            }

            id = (id << 8) | (byte)b;
        }

        return id;
    }

    // Reads an EBML size vint (1-8 bytes, leading marker stripped). Returns -1 on EOF/error and -2 for
    // the all-ones "unknown size" encoding (which we cannot seek past, so the caller stops).
    private static long ReadElementSize(Stream stream) {
        var first = stream.ReadByte();
        if (first < 0) {
            return -1;
        }

        var length = LeadingLength(first);
        if (length is < 1 or > 8) {
            return -1;
        }

        long mask = (1L << (8 - length)) - 1;
        var value = first & mask;
        var allOnes = value == mask;
        for (var i = 1; i < length; i++) {
            var b = stream.ReadByte();
            if (b < 0) {
                return -1;
            }

            value = (value << 8) | (byte)b;
            allOnes = allOnes && b == 0xFF;
        }

        return allOnes ? -2 : value;
    }

    private static int LeadingLength(int firstByte) {
        for (var i = 0; i < 8; i++) {
            if ((firstByte & (0x80 >> i)) != 0) {
                return i + 1;
            }
        }

        return -1;
    }
}
