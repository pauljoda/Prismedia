namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of the fields a custom-format condition tests a release against (the Sonarr custom-format
/// specification vocabulary, reduced to the axes Prismedia detects from a release title). Each condition
/// carries a value interpreted per type: a regex for <see cref="ReleaseTitle"/>/<see cref="ReleaseGroup"/>,
/// a canonical language name for <see cref="Language"/>, and an exact ladder quality code for
/// <see cref="Quality"/>.
/// </summary>
public enum CustomFormatConditionType {
    /// <summary>A regex matched (case-insensitively) against the whole release title.</summary>
    [Code("release-title")]
    ReleaseTitle,

    /// <summary>A regex matched against the detected release group (the trailing <c>-GROUP</c> segment).</summary>
    [Code("release-group")]
    ReleaseGroup,

    /// <summary>A canonical language name matched against the languages the release declares.</summary>
    [Code("language")]
    Language,

    /// <summary>An exact quality code on the kind's video/audio ladder, matched against the detected quality.</summary>
    [Code("quality")]
    Quality
}
