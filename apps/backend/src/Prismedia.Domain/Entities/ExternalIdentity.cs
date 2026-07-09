namespace Prismedia.Domain.Entities;

/// <summary>
/// Identifies an entity in an external system through a normalized namespace and an opaque,
/// provider-owned value. URLs are not identities and belong in <see cref="EntityUrl"/> or on
/// <see cref="EntityExternalId.Url"/>.
/// </summary>
/// <remarks>
/// Namespace comparison is case-insensitive by construction because namespaces are trimmed and
/// normalized to lower case. Identity values are trimmed but otherwise remain case-sensitive and
/// are never parsed or rewritten by Prismedia.
/// </remarks>
public sealed record ExternalIdentity {
    /// <summary>Creates a validated external identity.</summary>
    /// <param name="namespace">Stable external namespace, such as <c>tmdb</c> or <c>openlibrary</c>.</param>
    /// <param name="value">Opaque identifier assigned by the external namespace.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when either component is blank or when <paramref name="value"/> is URL-shaped.
    /// </exception>
    public ExternalIdentity(string @namespace, string value) {
        Namespace = NormalizeNamespace(@namespace);
        Value = NormalizeValue(value);
    }

    /// <summary>Normalized lower-case namespace that defines how <see cref="Value"/> is interpreted.</summary>
    public string Namespace { get; }

    /// <summary>Trimmed, case-preserving identifier owned by the external namespace.</summary>
    public string Value { get; }

    private static string NormalizeNamespace(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException("External identity namespace cannot be blank.", "namespace");
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!IsAsciiLetterOrDigit(normalized[0]) ||
            normalized.Any(character => !IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.')) {
            throw new ArgumentException(
                "External identity namespaces must start with a letter or digit and contain only letters, digits, '.', '_' or '-'.",
                "namespace");
        }

        return normalized;
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static string NormalizeValue(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException("External identity value cannot be blank.", nameof(value));
        }

        var normalized = value.Trim();
        if (LooksLikeUrl(normalized)) {
            throw new ArgumentException("External identity values cannot be URLs; store the URL separately.", nameof(value));
        }

        return normalized;
    }

    private static bool LooksLikeUrl(string value) {
        if (value.StartsWith("//", StringComparison.Ordinal) ||
            value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        var separatorIndex = value.IndexOf("://", StringComparison.Ordinal);
        return separatorIndex > 0 && Uri.CheckSchemeName(value[..separatorIndex]);
    }
}
