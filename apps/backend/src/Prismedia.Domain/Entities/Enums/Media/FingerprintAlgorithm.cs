namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of hash or fingerprint algorithms used for media file identification.
/// </summary>
public enum FingerprintAlgorithm {
    /// <summary>Standard MD5 content hash.</summary>
    [Code("md5")]
    Md5,

    /// <summary>OpenSubtitles hash — fast size+sample-based fingerprint.</summary>
    [Code("oshash")]
    Oshash,

    /// <summary>Perceptual hash for visual similarity matching.</summary>
    [Code("phash")]
    Phash
}
