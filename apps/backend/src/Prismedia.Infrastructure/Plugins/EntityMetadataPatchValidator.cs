using Prismedia.Contracts.Plugins;

namespace Prismedia.Infrastructure.Plugins;

/// <summary>
/// Validates the field-scoped metadata patch contract before persistence mutates entity rows.
/// </summary>
public static class EntityMetadataPatchValidator {
    /// <summary>
    /// Normalizes selected field keys into a case-insensitive set.
    /// </summary>
    public static HashSet<string> NormalizeFieldSet(IEnumerable<string> fields) =>
        fields
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Select(field => field.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Throws when a selected field would apply invalid metadata.
    /// </summary>
    public static void Validate(ISet<string> fields, EntityMetadataPatch patch) {
        var errors = new List<string>();

        if (fields.Contains("title") && string.IsNullOrWhiteSpace(patch.Title)) {
            errors.Add("title is required");
        }

        if (fields.Contains("rating") && patch.Rating is not null and (< 0 or > 5)) {
            errors.Add("rating must be from 0 through 5");
        }

        if (fields.Contains("urls")) {
            foreach (var url in patch.Urls.Where(value => !string.IsNullOrWhiteSpace(value))) {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                    parsed.Scheme is not ("http" or "https")) {
                    errors.Add($"url '{url}' must be an absolute http or https URL");
                }
            }
        }

        if (fields.Contains("dates")) {
            foreach (var (code, value) in patch.Dates) {
                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(value)) {
                    errors.Add("date codes and values cannot be empty");
                } else if (!DateOnly.TryParse(value, out _)) {
                    errors.Add($"date '{code}' must be parseable as a date");
                }
            }
        }

        if (errors.Count > 0) {
            throw new ArgumentException($"Invalid entity metadata patch: {string.Join("; ", errors)}.");
        }
    }
}
