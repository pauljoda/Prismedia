using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Prismedia.Infrastructure.StashCompat.Model;

namespace Prismedia.Infrastructure.StashCompat;

/// <summary>
/// Applies Stash selector post-processing: <c>common</c> variable substitution and the
/// ordered <c>postProcess</c> pipeline (replace, subString, map, parseDate, split, lbToSpace).
/// Faithfully ports the reference engine, with regex matching in Singleline (dotall) mode so
/// <c>.</c> spans newlines — load-bearing for extracting values from multiline script blocks.
/// </summary>
public static class StashSelector {
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Substitutes <c>common</c> block variables into a selector string. Stash treats the
    /// common map as literal text replacement (keys such as <c>$base</c> embedded in selectors).
    /// </summary>
    /// <param name="selector">Raw selector text.</param>
    /// <param name="common">The scraper block's <c>common</c> node.</param>
    /// <returns>The selector with all common variables expanded.</returns>
    public static string ApplyCommon(string selector, StashYamlNode common) {
        if (common.IsMissing || string.IsNullOrEmpty(selector)) {
            return selector;
        }

        var result = selector;
        foreach (var (key, value) in common.Entries()) {
            if (value.Scalar is { } replacement) {
                result = result.Replace(key, replacement, StringComparison.Ordinal);
            }
        }

        return result;
    }

    /// <summary>
    /// Runs the ordered <c>postProcess</c> pipeline over a scraped string value.
    /// </summary>
    /// <param name="value">Raw extracted value.</param>
    /// <param name="postProcess">The field's <c>postProcess</c> list node (may be missing).</param>
    /// <returns>The transformed value (trimmed); empty when an operation clears it.</returns>
    public static string ApplyPostProcess(string value, StashYamlNode postProcess) {
        var result = value;
        foreach (var rule in postProcess.Items()) {
            foreach (var (operation, argument) in rule.Entries()) {
                result = Apply(operation, argument, result);
            }
        }

        return result.Trim();
    }

    private static string Apply(string operation, StashYamlNode argument, string value) =>
        operation.ToLowerInvariant() switch {
            "replace" => ApplyReplace(value, argument),
            "substring" => ApplySubString(value, argument),
            "map" => ApplyMap(value, argument),
            "parsedate" => ApplyParseDate(value, argument.Scalar),
            "split" => ApplySplit(value, argument.Scalar),
            "lbtospace" => value.Replace('\n', ' ').Replace('\r', ' '),
            _ => value
        };

    private static string ApplyReplace(string value, StashYamlNode replacements) {
        var result = value;
        foreach (var rule in replacements.Items()) {
            var pattern = rule.StringAt("regex");
            if (string.IsNullOrEmpty(pattern)) {
                continue;
            }

            // Stash uses Go regexp, whose $1 capture-group references match .NET's syntax, so the
            // replacement template passes through unchanged.
            var replacement = rule["with"].Scalar ?? string.Empty;
            try {
                var regex = new Regex(pattern, RegexOptions.Singleline, RegexTimeout);
                if (regex.IsMatch(result)) {
                    result = regex.Replace(result, replacement);
                }
            } catch (RegexParseException) {
                // Leave the value unchanged when a scraper ships an invalid pattern.
            } catch (RegexMatchTimeoutException) {
            }
        }

        return result;
    }

    private static string ApplySubString(string value, StashYamlNode argument) {
        var start = ParseInt(argument["start"].Scalar) ?? 0;
        if (start < 0 || start >= value.Length) {
            return start >= value.Length ? string.Empty : value;
        }

        var end = ParseInt(argument["end"].Scalar);
        if (end is null) {
            return value[start..];
        }

        var stop = Math.Min(end.Value, value.Length);
        return stop <= start ? string.Empty : value[start..stop];
    }

    private static string ApplyMap(string value, StashYamlNode map) {
        foreach (var (key, mapped) in map.Entries()) {
            if (string.Equals(key, value, StringComparison.Ordinal) && mapped.Scalar is { } replacement) {
                return replacement;
            }
        }

        return value;
    }

    private static string ApplySplit(string value, string? separator) {
        if (string.IsNullOrEmpty(separator)) {
            return value;
        }

        var index = value.IndexOf(separator, StringComparison.Ordinal);
        return index < 0 ? value : value[..index];
    }

    /// <summary>
    /// Parses a value using a Go reference layout (e.g. <c>2006-01-02</c>, <c>January 2, 2006</c>)
    /// and normalizes it to <c>yyyy-MM-dd</c>. Falls back to common formats, then to the trimmed
    /// input, so a date that is already well-formed survives unchanged.
    /// </summary>
    private static string ApplyParseDate(string value, string? goLayout) {
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed)) {
            return trimmed;
        }

        var formats = new List<string>();
        if (!string.IsNullOrWhiteSpace(goLayout)) {
            formats.Add(GoLayoutToDotNet(goLayout));
        }

        formats.AddRange(["yyyy-MM-dd", "yyyy/MM/dd", "MM/dd/yyyy", "MMMM d, yyyy", "MMM d, yyyy", "d MMMM yyyy"]);

        foreach (var format in formats.Where(format => !string.IsNullOrWhiteSpace(format))) {
            if (DateTime.TryParseExact(trimmed, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) {
                return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback)
            ? fallback.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : trimmed;
    }

    private static string GoLayoutToDotNet(string layout) {
        // Translate the Go reference-time tokens used by Stash date layouts into .NET format tokens.
        var builder = new StringBuilder(layout);
        var replacements = new (string Go, string Net)[] {
            ("2006", "yyyy"),
            ("January", "MMMM"),
            ("Jan", "MMM"),
            ("Monday", "dddd"),
            ("Mon", "ddd"),
            ("15", "HH"),
            ("01", "MM"),
            ("02", "dd"),
            ("03", "hh"),
            ("04", "mm"),
            ("05", "ss"),
            ("06", "yy"),
            ("PM", "tt"),
            ("_2", "d"),
            ("1", "M"),
            ("2", "d"),
            ("3", "h"),
            ("4", "m"),
            ("5", "s")
        };

        var result = layout;
        foreach (var (go, net) in replacements) {
            result = result.Replace(go, net, StringComparison.Ordinal);
        }

        return result;
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
