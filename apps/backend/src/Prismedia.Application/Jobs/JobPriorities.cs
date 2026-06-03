namespace Prismedia.Application.Jobs;

/// <summary>
/// Background job priorities. The queue claims the highest priority first, so these are ordered to
/// make the library usable as fast as possible after a scan: scanning creates the lightweight
/// entities the UI shows, then fast metadata queries and the quick static thumbnails that give cards
/// a cover, then metadata sidecars, leaving the heavy preview-clip and trickplay generation last so a
/// large backlog of it never delays newly added media from appearing.
/// </summary>
public static class JobPriorities {
    /// <summary>Library scan — creates the entities the UI shows. Highest so new media appears fast.</summary>
    public const int Scan = 60;

    /// <summary>Technical metadata probe (ffprobe). A fast query other work depends on.</summary>
    public const int Probe = 50;

    /// <summary>Fingerprinting (oshash / MD5 / perceptual hash). A fast query.</summary>
    public const int Fingerprint = 45;

    /// <summary>Quick static thumbnails (grid, image, book page and cover) — generated with Skia.</summary>
    public const int Thumbnail = 40;

    /// <summary>Audio waveform peak data — a quick build.</summary>
    public const int Waveform = 35;

    /// <summary>Metadata sidecars such as embedded subtitle extraction.</summary>
    public const int Sidecar = 30;

    /// <summary>Auto Identify — provider metadata lookups that fill and organize matched media.</summary>
    public const int AutoIdentify = 20;

    /// <summary>Video preview clip and trickplay sprite generation — the heaviest work. Lowest.</summary>
    public const int Preview = 10;
}
