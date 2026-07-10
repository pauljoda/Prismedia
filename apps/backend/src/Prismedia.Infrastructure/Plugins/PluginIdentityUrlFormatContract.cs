using System.Text;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Parses and applies the deliberately small identity URL template language. Matching is performed
/// without regular expressions so plugin-authored patterns cannot introduce backtracking work.
/// </summary>
internal static class PluginIdentityUrlFormatContract {
    private const int MaximumValuePatternLength = 512;
    private const int MaximumUrlTemplateLength = 2048;

    /// <summary>Returns whether a format is safe and belongs to a declared identity namespace.</summary>
    internal static bool IsValid(
        PluginIdentityUrlFormat? format,
        IReadOnlySet<string> declaredNamespaces) {
        if (format is null ||
            !declaredNamespaces.Contains(format.IdentityNamespace) ||
            !IsTrimmedNonEmpty(format.IdentityNamespace) ||
            !IsTrimmedNonEmpty(format.ValuePattern) ||
            format.ValuePattern.Length > MaximumValuePatternLength ||
            !IsTrimmedNonEmpty(format.UrlTemplate) ||
            format.UrlTemplate.Length > MaximumUrlTemplateLength ||
            !TryParse(format.ValuePattern, rejectAdjacentTokens: true, rejectDuplicateTokens: true, out var valueParts) ||
            !TryParse(format.UrlTemplate, rejectAdjacentTokens: false, rejectDuplicateTokens: false, out var urlParts)) {
            return false;
        }

        if (!UsesSameTokenSet(valueParts, urlParts)) {
            return false;
        }

        var valueTokens = valueParts
            .Where(part => part.IsToken)
            .Select(part => part.Value)
            .ToHashSet(StringComparer.Ordinal);
        var sampleCaptures = valueTokens.ToDictionary(
            token => token,
            _ => "sample",
            StringComparer.Ordinal);
        return TryRender(urlParts, sampleCaptures, out _);
    }

    /// <summary>
    /// Matches the complete opaque identity value and safely renders the corresponding URL.
    /// This repeats all validation so callers fail closed even when handed an unvalidated catalog.
    /// </summary>
    internal static bool TryBuild(
        PluginIdentityUrlFormat? format,
        string identityValue,
        out string url) {
        url = string.Empty;
        if (format is null ||
            !IsTrimmedNonEmpty(identityValue) ||
            identityValue.Length > MaximumValuePatternLength ||
            !IsTrimmedNonEmpty(format.IdentityNamespace) ||
            !IsTrimmedNonEmpty(format.ValuePattern) ||
            format.ValuePattern.Length > MaximumValuePatternLength ||
            !IsTrimmedNonEmpty(format.UrlTemplate) ||
            format.UrlTemplate.Length > MaximumUrlTemplateLength ||
            !TryParse(format.ValuePattern, rejectAdjacentTokens: true, rejectDuplicateTokens: true, out var valueParts) ||
            !TryParse(format.UrlTemplate, rejectAdjacentTokens: false, rejectDuplicateTokens: false, out var urlParts) ||
            !UsesSameTokenSet(valueParts, urlParts) ||
            !TryCapture(valueParts, identityValue, out var captures)) {
            return false;
        }

        return TryRender(urlParts, captures, out url);
    }

    private static bool TryParse(
        string template,
        bool rejectAdjacentTokens,
        bool rejectDuplicateTokens,
        out IReadOnlyList<TemplatePart> parts) {
        var parsed = new List<TemplatePart>();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var literalStart = 0;

        for (var index = 0; index < template.Length; index++) {
            var character = template[index];
            if (character == '}') {
                parts = [];
                return false;
            }

            if (character != '{') {
                continue;
            }

            if (index > literalStart) {
                parsed.Add(new TemplatePart(IsToken: false, template[literalStart..index]));
            }

            var close = template.IndexOf('}', index + 1);
            if (close < 0 || template.IndexOf('{', index + 1, close - index - 1) >= 0) {
                parts = [];
                return false;
            }

            var token = template[(index + 1)..close];
            if (!IsStableToken(token) ||
                rejectDuplicateTokens && !tokens.Add(token) ||
                rejectAdjacentTokens && parsed.Count > 0 && parsed[^1].IsToken) {
                parts = [];
                return false;
            }

            tokens.Add(token);
            parsed.Add(new TemplatePart(IsToken: true, token));
            index = close;
            literalStart = close + 1;
        }

        if (literalStart < template.Length) {
            parsed.Add(new TemplatePart(IsToken: false, template[literalStart..]));
        }

        parts = parsed;
        return parsed.Count > 0;
    }

    private static bool TryCapture(
        IReadOnlyList<TemplatePart> pattern,
        string value,
        out IReadOnlyDictionary<string, string> captures) {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var cursor = 0;

        for (var index = 0; index < pattern.Count; index++) {
            var part = pattern[index];
            if (!part.IsToken) {
                if (!value.AsSpan(cursor).StartsWith(part.Value, StringComparison.Ordinal)) {
                    captures = found;
                    return false;
                }

                cursor += part.Value.Length;
                continue;
            }

            var nextLiteral = index + 1 < pattern.Count && !pattern[index + 1].IsToken
                ? pattern[index + 1].Value
                : null;
            var captureEnd = nextLiteral is null
                ? value.Length
                : value.IndexOf(nextLiteral, cursor, StringComparison.Ordinal);
            if (captureEnd < cursor || captureEnd == cursor) {
                captures = found;
                return false;
            }

            found.Add(part.Value, value[cursor..captureEnd]);
            cursor = captureEnd;
        }

        captures = found;
        return cursor == value.Length;
    }

    private static bool TryRender(
        IReadOnlyList<TemplatePart> template,
        IReadOnlyDictionary<string, string> captures,
        out string url) {
        var builder = new StringBuilder();
        foreach (var part in template) {
            if (!part.IsToken) {
                builder.Append(part.Value);
                continue;
            }

            if (!captures.TryGetValue(part.Value, out var capture)) {
                url = string.Empty;
                return false;
            }

            builder.Append(Uri.EscapeDataString(capture));
        }

        var candidate = builder.ToString();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !(uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
              uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo)) {
            url = string.Empty;
            return false;
        }

        url = uri.AbsoluteUri;
        return true;
    }

    private static bool IsTrimmedNonEmpty(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static bool IsStableToken(string token) =>
        token.Length > 0 &&
        IsAsciiLetter(token[0]) &&
        token.All(character =>
            IsAsciiLetter(character) ||
            character is >= '0' and <= '9' or '-' or '_' or '.');

    private static bool IsAsciiLetter(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static bool UsesSameTokenSet(
        IReadOnlyList<TemplatePart> valueParts,
        IReadOnlyList<TemplatePart> urlParts) {
        var valueTokens = valueParts
            .Where(part => part.IsToken)
            .Select(part => part.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (valueTokens.Count == 0) {
            return false;
        }

        var urlTokens = urlParts
            .Where(part => part.IsToken)
            .Select(part => part.Value)
            .ToHashSet(StringComparer.Ordinal);
        return valueTokens.SetEquals(urlTokens);
    }

    private sealed record TemplatePart(bool IsToken, string Value);
}
