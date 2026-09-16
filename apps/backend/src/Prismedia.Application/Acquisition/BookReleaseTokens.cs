using System.Text.RegularExpressions;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Parses the book/comic unit a release or file name declares — the "Vol 3" / "Volume 3" / "v03"
/// conventions comic and manga releases use. One decode site shared by the book decision engine (does
/// this release name the volume we seek?) and its ranking (an exact-volume release outranks a
/// volume-less one). The bare <c>v</c> form requires two or more digits (<c>v03</c>) so it can never
/// collide with anime-style revision markers (<c>v2</c> = second cut of the same episode).
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
}
