namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of library-root media flags used when selecting a compatible request import target.
/// The codes intentionally match the public library-root contract property names.
/// </summary>
public enum LibraryRootMediaCapability {
    /// <summary>The root accepts book and comic scans.</summary>
    [Code("scanBooks")]
    ScanBooks,

    /// <summary>The root accepts video scans.</summary>
    [Code("scanVideos")]
    ScanVideos,

    /// <summary>The root accepts audio scans.</summary>
    [Code("scanAudio")]
    ScanAudio
}
