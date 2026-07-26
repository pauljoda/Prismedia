namespace Prismedia.Contracts.Plugins;

/// <summary>
/// Stable identifiers for bundled metadata plugins that receive host-specific presentation or
/// scheduling behavior. The plugin catalog remains extensible; these constants prevent duplicating
/// identifiers for the small set the host deliberately recognizes.
/// </summary>
public static class WellKnownPluginIds {
    public const string MusicBrainz = "musicbrainz";
    public const string MangaDex = "mangadex";
    public const string OpenLibrary = "openlibrary";
}
