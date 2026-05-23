namespace Prismedia.Domain.Capabilities;

/// <summary>
/// Mutable technical metadata capability for media-like entities.
/// </summary>
public sealed class CapabilityTechnical : EntityCapability {
    /// <summary>Media duration when known.</summary>
    public TimeSpan? Duration { get; private set; }

    /// <summary>Pixel width when known.</summary>
    public int? Width { get; private set; }

    /// <summary>Pixel height when known.</summary>
    public int? Height { get; private set; }

    /// <summary>Video frame rate when known.</summary>
    public double? FrameRate { get; private set; }

    /// <summary>Media bit rate when known.</summary>
    public int? BitRate { get; private set; }

    /// <summary>Audio sample rate when known.</summary>
    public int? SampleRate { get; private set; }

    /// <summary>Audio channel count when known.</summary>
    public int? Channels { get; private set; }

    /// <summary>Primary codec when known.</summary>
    public string? Codec { get; private set; }

    /// <summary>Container name when known.</summary>
    public string? Container { get; private set; }

    /// <summary>Format name when known.</summary>
    public string? Format { get; private set; }

    /// <summary>
    /// Replaces the technical metadata with values produced by a media probe.
    /// </summary>
    /// <param name="duration">Media duration when known.</param>
    /// <param name="width">Pixel width when known.</param>
    /// <param name="height">Pixel height when known.</param>
    /// <param name="frameRate">Video frame rate when known.</param>
    /// <param name="bitRate">Media bit rate when known.</param>
    /// <param name="sampleRate">Audio sample rate when known.</param>
    /// <param name="channels">Audio channel count when known.</param>
    /// <param name="codec">Primary codec when known.</param>
    /// <param name="container">Container name when known.</param>
    /// <param name="format">Format name when known.</param>
    public void Apply(
        TimeSpan? duration = null,
        int? width = null,
        int? height = null,
        double? frameRate = null,
        int? bitRate = null,
        int? sampleRate = null,
        int? channels = null,
        string? codec = null,
        string? container = null,
        string? format = null) {
        Duration = duration;
        Width = width;
        Height = height;
        FrameRate = frameRate;
        BitRate = bitRate;
        SampleRate = sampleRate;
        Channels = channels;
        Codec = codec;
        Container = container;
        Format = format;
    }
}
