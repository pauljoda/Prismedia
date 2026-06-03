using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Normalizes an entity title into a provider-agnostic identify search query.
/// Strips parenthetical/bracketed grouping tokens — release years, quality tags,
/// and scene-group suffixes such as <c>(1960)</c>, <c>[1080p]</c>, or <c>{GROUP}</c> —
/// and flattens diacritics so accented titles match plain-ASCII provider catalogs
/// (for example <c>Pokémon</c> searches as <c>Pokemon</c>).
/// </summary>
/// <remarks>
/// Applied to <see cref="Prismedia.Contracts.Plugins.IdentifyMatchHints.Title"/>, the shared
/// search fallback every metadata provider reads, so cleaning happens once in core rather than
/// being reinvented per plugin. User-typed query overrides are never normalized here; the user's
/// exact text is sent as entered.
/// </remarks>
public static partial class IdentifyQueryNormalizer {
    /// <summary>
    /// Produces a cleaned search query from a raw entity title.
    /// </summary>
    /// <param name="title">Raw entity title; may be null or blank.</param>
    /// <returns>
    /// The cleaned query, or the original (trimmed) value when null/blank or when cleaning would
    /// empty the string — so a title that is entirely a bracketed token still yields a usable query.
    /// </returns>
    public static string? NormalizeForSearch(string? title) {
        if (string.IsNullOrWhiteSpace(title)) {
            return title;
        }

        var withoutGroups = GroupingTokenRegex().Replace(title, " ");
        var folded = FoldDiacritics(withoutGroups);
        var collapsed = WhitespaceRegex().Replace(folded, " ").Trim();

        return collapsed.Length == 0 ? title.Trim() : collapsed;
    }

    /// <summary>
    /// Removes combining diacritic marks by decomposing to NFD, dropping non-spacing marks, and
    /// recomposing. Characters that have no canonical decomposition (for example ø, ł) are left as-is.
    /// </summary>
    private static string FoldDiacritics(string value) {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed) {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[\(\[\{][^\)\]\}]*[\)\]\}]", RegexOptions.CultureInvariant)]
    private static partial Regex GroupingTokenRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
