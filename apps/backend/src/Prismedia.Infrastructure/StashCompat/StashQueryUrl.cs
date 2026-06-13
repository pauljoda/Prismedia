using System.Text.RegularExpressions;
using Prismedia.Infrastructure.StashCompat.Model;

namespace Prismedia.Infrastructure.StashCompat;

/// <summary>
/// Builds a fetch URL from a Stash <c>queryURL</c> template and its <c>queryURLReplace</c> rules.
/// Placeholders (<c>{filename}</c>, <c>{title}</c>, <c>{url}</c>, <c>{checksum}</c>, <c>{oshash}</c>)
/// are sourced from the lookup input; each placeholder's ordered regex rules apply
/// the first match before substitution, matching the reference engine.
/// </summary>
public static class StashQueryUrl {
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Builds the query URL, or null when a required placeholder value is missing.
    /// </summary>
    /// <param name="template">The <c>queryURL</c> template.</param>
    /// <param name="replacements">The <c>queryURLReplace</c> node (placeholder → ordered rules).</param>
    /// <param name="input">Lookup inputs supplying placeholder values.</param>
    /// <returns>The resolved URL, or null when it cannot be fully built.</returns>
    public static string? Build(string template, StashYamlNode replacements, StashScrapeInput input) {
        var url = template;

        foreach (var placeholder in Placeholders(template)) {
            var value = SourceValue(placeholder, input);
            if (string.IsNullOrEmpty(value)) {
                return null;
            }

            value = ApplyReplacements(value, replacements[placeholder]);
            if (string.IsNullOrEmpty(value)) {
                return null;
            }

            url = url.Replace($"{{{placeholder}}}", value, StringComparison.Ordinal);
        }

        if (url.Contains("{}", StringComparison.Ordinal)) {
            var query = FirstNonEmpty(input.Title, input.Url, input.FilePath);
            if (string.IsNullOrWhiteSpace(query)) {
                return null;
            }

            url = url.Replace("{}", Uri.EscapeDataString(query), StringComparison.Ordinal);
        }

        return url;
    }

    private static IEnumerable<string> Placeholders(string template) {
        foreach (Match match in Regex.Matches(template, "\\{([a-zA-Z_]+)\\}")) {
            yield return match.Groups[1].Value;
        }
    }

    private static string? SourceValue(string placeholder, StashScrapeInput input) =>
        placeholder.ToLowerInvariant() switch {
            "filename" => string.IsNullOrEmpty(input.FilePath)
                ? null
                : input.FilePath.Replace('\\', '/').Split('/').LastOrDefault(),
            "title" => input.Title,
            "url" => input.Url,
            "checksum" => input.Checksum,
            "oshash" => input.Oshash,
            _ => null
        };

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string ApplyReplacements(string value, StashYamlNode rules) {
        foreach (var rule in rules.Items()) {
            var pattern = rule.StringAt("regex");
            if (string.IsNullOrEmpty(pattern)) {
                continue;
            }

            var replacement = rule["with"].Scalar ?? string.Empty;
            try {
                var regex = new Regex(pattern, RegexOptions.Singleline, RegexTimeout);
                if (regex.IsMatch(value)) {
                    // Stash applies the first matching replacement, then stops.
                    return regex.Replace(value, replacement);
                }
            } catch (RegexParseException) {
            } catch (RegexMatchTimeoutException) {
            }
        }

        return value;
    }
}
