using System.Globalization;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Starter rules for the basic acquisition editor. Every preset becomes an ordinary custom format:
/// there is no separate matcher or immutable built-in rule state after the user creates it. Each preset
/// names the profile kinds it fits, so video codecs are not offered to Book profiles and audiobook rules
/// are not offered to Movie profiles.
/// </summary>
public static class AcquisitionRulePresets {
    /// <summary>
    /// Lists editable language and release-format preferences with conservative starting weights. With
    /// <paramref name="profileKind"/>, only the presets that fit the profile governing that kind are listed.
    /// </summary>
    public static IReadOnlyList<AcquisitionRulePresetView> List(EntityKind? profileKind = null) {
        var presets = Build();
        return profileKind is { } kind
            ? presets.Where(preset => preset.ProfileKinds.Contains(AcquisitionProfileKinds.For(kind))).ToArray()
            : presets;
    }

    private static IReadOnlyList<AcquisitionRulePresetView> Build() {
        var everyProfile = AcquisitionProfileKinds.All;
        var videoProfiles = ProfilesScanning(LibraryRootMediaCapability.ScanVideos);
        var musicProfiles = ProfilesScanning(LibraryRootMediaCapability.ScanAudio);
        var audiobookProfiles = RequestKindRegistry.All
            .Where(descriptor => descriptor.BookRendition == BookRendition.Audiobook)
            .Select(descriptor => descriptor.ProfileEntityKind)
            .OfType<EntityKind>()
            .Distinct()
            .ToArray();
        return [
            .. ReleaseLanguageDetection.KnownLanguages.Select(language => new AcquisitionRulePresetView(
                CultureInfo.InvariantCulture.TextInfo.ToTitleCase(language) + " audio",
                "Matches explicitly named audio, including recognized language codes. Subtitle labels do not count.",
                100, [new(CustomFormatConditionType.Language, language, false, true)], everyProfile, language)),
            new("Unspecified multiple audio languages", "A fallback hint only. MULTI and Dual Audio do not confirm any particular language.",
                25, [new(CustomFormatConditionType.Language, ReleaseLanguageDetection.Multi, false, true)], everyProfile),
            Title(videoProfiles, "H.265 / HEVC", "Prefer releases naming H.265, x265 or HEVC encoding.", @"(?:[hx][ ._-]?265|hevc)"),
            Title(videoProfiles, "H.264 / AVC", "Prefer releases naming H.264, x264 or AVC encoding.", @"(?:[hx][ ._-]?264|avc)"),
            Title(videoProfiles, "AV1", "Prefer releases naming AV1 encoding. Check your playback device's support.", @"av1"),
            Title(videoProfiles, "HDR10", "Prefer an explicit HDR10 or HDR10+ label; an unspecified HDR label does not confirm HDR10.", @"hdr[ ._-]?10(?:\+)?"),
            Title(videoProfiles, "Dolby Vision", "Prefer DV, DoVi or Dolby Vision labels. Confirm playback support for your devices.", @"(?:dv|dovi|dolby[ ._-]?vision)"),
            Title(videoProfiles, "Web download", "Prefer WEB-DL and WEBDL releases.", @"web[ ._-]?dl"),
            Title(videoProfiles, "Blu-ray remux", "Prefer releases explicitly marked as a remux.", @"(?:bd)?remux"),
            Title(musicProfiles, "Lossless audio", "Prefer FLAC, ALAC or lossless labels.", @"(?:flac|alac|lossless)"),
            Title(videoProfiles, "Avoid upscales", "Penalize explicit upscale labels. The profile's minimum format score determines rejection.", @"upscal(?:e|ed|ing)", -100),
            Title(audiobookProfiles, "M4B audiobook", "Prefer audiobooks released as M4B, which usually carry chapter markers.", @"m4b"),
            Title(audiobookProfiles, "Chapterized audiobook", "Prefer audiobooks that say they are chapterized or split by chapter.",
                @"(?:chapteri[sz]ed|chaptered|with[ ._-]?chapters|split[ ._-]?by[ ._-]?chapters?)")
        ];
    }

    private static IReadOnlyList<EntityKind> ProfilesScanning(LibraryRootMediaCapability capability) =>
        AcquisitionProfileKinds.All
            .Where(kind => EntityKindRegistry.Describe(kind).AcquisitionProfile?.LibraryRootMediaCapability == capability)
            .ToArray();

    private static AcquisitionRulePresetView Title(
        IReadOnlyList<EntityKind> profileKinds, string name, string description, string pattern, int score = 100) =>
        new(name, description, score,
            [new(CustomFormatConditionType.ReleaseTitle, @"(?<![\p{L}\p{N}])(?:" + pattern + @")(?![\p{L}\p{N}])", false, true)],
            profileKinds);
}
