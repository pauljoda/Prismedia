namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of subtitle rendering styles supported by the playback UI.
/// </summary>
public enum SubtitleStyle {
    /// <summary>Prismedia's styled subtitle presentation.</summary>
    [Code("stylized")]
    Stylized,

    /// <summary>Plain browser-like subtitle presentation.</summary>
    [Code("plain")]
    Plain
}
