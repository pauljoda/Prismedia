using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Prismedia.Application.Acquisition;

/// <summary>Offers bounded subject-format fallbacks without altering the displayed release or its download identity.</summary>
internal static partial class ReleaseSubjectTitle {
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Tries the original first, then a complete quoted multipart subject, and at most two reversible
    /// UTF-8-as-Latin-1 decoding repairs. Every variant still needs the caller's full identity checks.
    /// Unrecognized prefixes and lossy byte conversions never become identity evidence.
    /// </summary>
    public static IEnumerable<string> Variants(string value) {
        yield return value;
        if (value.Length > 4096) yield break;
        var subject = MultipartSubject().Match(value);
        if (subject.Success
            && int.Parse(subject.Groups[1].Value, CultureInfo.InvariantCulture) is > 0 and var part
            && int.Parse(subject.Groups[2].Value, CultureInfo.InvariantCulture) >= part) {
            value = subject.Groups[3].Value;
            yield return value;
        }
        for (var pass = 0; pass < 2; pass++) {
            var repaired = RepairEncoding(value);
            if (repaired is null) yield break;
            yield return repaired;
            value = repaired;
        }
    }

    private static string? RepairEncoding(string value) {
        if (!value.Any(character => character > 0x7f) || value.Any(character => character > 0xff)) return null;
        try {
            return StrictUtf8.GetString(Encoding.Latin1.GetBytes(value));
        } catch (DecoderFallbackException) {
            return null;
        }
    }

    // prism-vocab: external Usenet subject syntax; only the complete quoted subject is unwrapped.
    [GeneratedRegex("^\\s*\\[([0-9]{1,7})/([0-9]{1,7})\\]\\s*\"([^\"\\r\\n]+)\"(?:\\s+yEnc)?\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MultipartSubject();
}
