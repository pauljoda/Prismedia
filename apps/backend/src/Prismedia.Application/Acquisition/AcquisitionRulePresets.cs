using System.Globalization;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Starter rules for the basic acquisition editor. Every preset becomes an ordinary custom format:
/// there is no separate matcher or immutable built-in rule state after the user creates it.
/// </summary>
public static class AcquisitionRulePresets {
    /// <summary>Lists editable language and release-format preferences with conservative starting weights.</summary>
    public static IReadOnlyList<AcquisitionRulePresetView> List() => [
        .. ReleaseLanguageDetection.KnownLanguages.Select(language => new AcquisitionRulePresetView(
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(language) + " audio", 
            "Matches explicitly named audio, including recognized language codes. Subtitle labels do not count.",
            100, [new(CustomFormatConditionType.Language, language, false, true)], language)),
        new("Unspecified multiple audio languages", "A fallback hint only. MULTI and Dual Audio do not confirm any particular language.",
            25, [new(CustomFormatConditionType.Language, ReleaseLanguageDetection.Multi, false, true)]),
        Title("H.265 / HEVC", "Prefer releases naming H.265, x265 or HEVC encoding.", @"(?:[hx][ ._-]?265|hevc)"),
        Title("H.264 / AVC", "Prefer releases naming H.264, x264 or AVC encoding.", @"(?:[hx][ ._-]?264|avc)"),
        Title("AV1", "Prefer releases naming AV1 encoding. Check your playback device's support.", @"av1"),
        Title("HDR10", "Prefer an explicit HDR10 or HDR10+ label; an unspecified HDR label does not confirm HDR10.", @"hdr[ ._-]?10(?:\+)?"),
        Title("Dolby Vision", "Prefer DV, DoVi or Dolby Vision labels. Confirm playback support for your devices.", @"(?:dv|dovi|dolby[ ._-]?vision)"),
        Title("Web download", "Prefer WEB-DL and WEBDL releases.", @"web[ ._-]?dl"),
        Title("Blu-ray remux", "Prefer releases explicitly marked as a remux.", @"(?:bd)?remux"),
        Title("Lossless audio", "Prefer FLAC, ALAC or lossless labels.", @"(?:flac|alac|lossless)"),
        Title("Avoid upscales", "Penalize explicit upscale labels. The profile's minimum format score determines rejection.", @"upscal(?:e|ed|ing)", -100)
    ];

    private static AcquisitionRulePresetView Title(string name, string description, string pattern, int score = 100) =>
        new(name, description, score, [new(CustomFormatConditionType.ReleaseTitle, @"(?<![\p{L}\p{N}])(?:" + pattern + @")(?![\p{L}\p{N}])", false, true)]);
}
