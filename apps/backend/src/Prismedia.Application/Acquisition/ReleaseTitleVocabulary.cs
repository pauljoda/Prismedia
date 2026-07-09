namespace Prismedia.Application.Acquisition;

/// <summary>
/// The shared release-title metadata vocabulary: normalized tokens that describe a release's quality,
/// source, codec, packaging, or language rather than the work it contains. One declaration site consumed
/// by relevance scoring (strip metadata before comparing content words) and title-identity gating (a
/// metadata token legitimately ends a title).
/// </summary>
public static class ReleaseTitleVocabulary {
    // prism-vocab: external — release-title vocabulary, declared only here.
    public static readonly IReadOnlySet<string> MetadataTokens = new HashSet<string>(StringComparer.Ordinal) {
        "480p", "480i", "540p", "576p", "720p", "1080p", "1080i", "1440p", "2160p", "4k", "uhd", "sdtv",
        "hdtv", "pdtv", "web", "dl", "webdl", "rip", "webrip", "dvd", "dvdrip", "bluray", "blu", "ray",
        "bdrip", "brrip", "bd25", "bd50", "remux", "hdr", "sdr", "dv", "x264", "x265", "h264", "h265",
        "hevc", "avc", "aac", "dts", "ddp", "atmos", "mp3", "flac", "alac", "opus", "ogg", "lossless",
        "hi", "res", "hires", "24", "bit", "24bit", "320", "256", "192", "v0",
        "proper", "repack", "rerip", "retail", "official", "digital", "converted", "calibre",
        "cbz", "zip", "epub", "pdf", "cbr", "rar", "mobi", "azw", "azw3",
        "english", "eng", "french", "fre", "fra", "german", "ger", "deu", "spanish", "spa", "ita", "multi",
        "complete", "season", "seasons", "pack"
    };
}
