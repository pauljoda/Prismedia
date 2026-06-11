namespace Prismedia.Application.Requests;

/// <summary>
/// Certifications that mark external provider results as adults-only. Mature-but-mainstream
/// ratings (R, TV-MA, BBFC 18) intentionally stay visible in SFW mode; only explicit adult
/// classifications are gated behind the NSFW toggle.
/// </summary>
public static class AdultCertifications {
    /// <summary>
    /// Certification applied to results whose source flags them adult (e.g. TMDB's
    /// <c>adult</c> boolean) without providing a concrete rating board code.
    /// </summary>
    public const string Implied = "XXX";

    private static readonly HashSet<string> Codes = new(StringComparer.OrdinalIgnoreCase) {
        "NC-17",
        "NC17",
        "X",
        "XX",
        "XXX",
        "AO",
        "R18",
        "R18+"
    };

    /// <summary>Returns true when the certification denotes adults-only content.</summary>
    public static bool IsAdult(string? certification) =>
        !string.IsNullOrWhiteSpace(certification) && Codes.Contains(certification.Trim());
}
