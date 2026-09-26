using System.Text.RegularExpressions;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Parses the book/comic unit a release or file name declares — the "Vol 3" / "Volume 3" / "v03"
/// conventions comic and manga releases use. One decode site shared by the book decision engine (does
/// this release name the volume we seek?) and its ranking (an exact-volume release outranks a
/// volume-less one). The bare <c>v</c> form requires two or more digits (<c>v03</c>) so it can never
/// collide with anime-style revision markers (<c>v2</c> = second cut of the same episode). Audiobook
/// file and folder markers (discs, parts, chapters) are decoded here too, for
/// <see cref="AudiobookReleaseShape"/> and the audiobook import order.
/// </summary>
public static partial class BookReleaseTokens {
    [GeneratedRegex(
        @"(?:^|[\s._\-(\[])(?:vol(?:ume)?\.?[\s._-]*(?<volume>\d{1,4})|v(?<volume>\d{2,4}))(?:\D|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VolumeTokenRegex();

    [GeneratedRegex(
        @"(?:^|[\s._\-(\[])(?:ch(?:apter)?|issue|ep(?:isode)?|#)\.?[\s._]*(?<installment>-?[0-9]+(?:\.[0-9]+)?(?:/[0-9]+)?[a-z]*(?:-[0-9]+(?:\.[0-9]+)?)?)(?![a-z0-9/]|\.[0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallmentTokenRegex();

    // prism-vocab: external — audiobook file and folder naming conventions, matched only here.
    [GeneratedRegex(
        @"(?:^|[\s._\-(\[])(?:cd|disc|disk)[\s._-]*(?<number>\d{1,3})(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AudioDiscTokenRegex();

    [GeneratedRegex(
        @"(?:(?:^|[\s._\-(\[])(?:part|pt|cd|disc|disk|track)[\s._-]*(?<number>\d{1,3})(?!\d)|(?:^|[\s._\-(\[])(?<number>\d{1,3})[\s._-]*of[\s._-]*\d{1,3}(?!\d))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AudioPartTokenRegex();

    [GeneratedRegex(
        @"(?:^|[\s._\-(\[])(?:(?:chapter|chap|ch)[\s._-]*\d{1,3}(?!\d)|(?:prologue|epilogue|introduction|foreword|preface|afterword|interlude|acknowledg(?:e)?ments|opening[\s._-]*credits|end[\s._-]*credits)(?![a-z]))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AudioChapterTokenRegex();

    /// <summary>The volume number a name declares, or null when it names none.</summary>
    public static int? ParseVolume(string name) {
        var match = VolumeTokenRegex().Match(name);
        return match.Success && int.TryParse(match.Groups["volume"].Value, out var volume) ? volume : null;
    }

    /// <summary>The independently released chapter/issue number a comic name declares, or null.</summary>
    public static ComicInstallmentNumber? ParseInstallment(string name) {
        var match = InstallmentTokenRegex().Match(name);
        return match.Success ? ComicInstallmentNumber.Parse(match.Groups["installment"].Value) : null;
    }

    /// <summary>
    /// The disc number a folder name declares ("CD1", "Disc 02", "Disk 3"), or null. A disc folder means
    /// the audiobook was ripped or split across media, so its files are parts rather than chapters.
    /// </summary>
    public static int? ParseAudioDisc(string folderName) => Number(AudioDiscTokenRegex().Match(folderName));

    /// <summary>
    /// The part number an audio file name declares ("Part01", "Pt 3", "CD2", "Track 07", "3 of 12"),
    /// or null. These names describe how a recording was split, not where its chapters begin.
    /// </summary>
    public static int? ParseAudioPart(string fileName) => Number(AudioPartTokenRegex().Match(fileName));

    /// <summary>
    /// Whether an audio file name marks a chapter ("Chapter 3", "Ch05", "Prologue", "Epilogue",
    /// "Opening Credits"), meaning the file was cut at a chapter boundary.
    /// </summary>
    public static bool NamesAudioChapter(string fileName) => AudioChapterTokenRegex().IsMatch(fileName);

    private static int? Number(Match match) =>
        match.Success && int.TryParse(match.Groups["number"].Value, out var number) ? number : null;
}
