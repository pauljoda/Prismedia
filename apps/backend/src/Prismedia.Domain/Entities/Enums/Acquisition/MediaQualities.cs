namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of video release qualities, ordered worst → best. One combined source × resolution
/// ladder (the Sonarr shape): a profile allows a subset, ranks candidates by ladder position, and
/// stops upgrading at its cutoff. <see cref="Unknown"/> ranks below everything.
/// </summary>
public enum VideoQuality {
    [Code("unknown")]
    Unknown,

    [Code("sdtv")]
    Sdtv,

    [Code("dvd")]
    Dvd,

    [Code("hdtv-720p")]
    Hdtv720p,

    [Code("webrip-720p")]
    Webrip720p,

    [Code("webdl-720p")]
    Webdl720p,

    [Code("bluray-720p")]
    Bluray720p,

    [Code("hdtv-1080p")]
    Hdtv1080p,

    [Code("webrip-1080p")]
    Webrip1080p,

    [Code("webdl-1080p")]
    Webdl1080p,

    [Code("bluray-1080p")]
    Bluray1080p,

    [Code("remux-1080p")]
    Remux1080p,

    [Code("hdtv-2160p")]
    Hdtv2160p,

    [Code("webrip-2160p")]
    Webrip2160p,

    [Code("webdl-2160p")]
    Webdl2160p,

    [Code("bluray-2160p")]
    Bluray2160p,

    [Code("remux-2160p")]
    Remux2160p
}

/// <summary>
/// Closed set of audio release qualities, ordered worst → best: unknown, ordinary lossy, high-bitrate
/// lossy (320/V0), lossless (FLAC/ALAC), and hi-res lossless (24-bit).
/// </summary>
public enum AudioQuality {
    [Code("unknown")]
    Unknown,

    [Code("lossy")]
    Lossy,

    [Code("lossy-high")]
    LossyHigh,

    [Code("lossless")]
    Lossless,

    [Code("lossless-hires")]
    LosslessHiRes
}
