using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

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

        if (fields.Contains(MetadataPatchField.Title.ToCode()) && string.IsNullOrWhiteSpace(patch.Title)) {
            errors.Add("title is required");
        }

        if (fields.Contains(MetadataPatchField.Urls.ToCode())) {
            foreach (var url in patch.Urls.Where(value => !string.IsNullOrWhiteSpace(value))) {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                    parsed.Scheme is not ("http" or "https")) {
                    errors.Add($"url '{url}' must be an absolute http or https URL");
                }
            }
        }

        if (fields.Contains(MetadataPatchField.Dates.ToCode())) {
            foreach (var (code, value) in EntityMetadataDateNormalization.Normalize(patch)) {
                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(value)) {
                    errors.Add("date codes and values cannot be empty");
                } else if (EntityDateParser.Parse(value) is null) {
                    errors.Add($"date '{code}' must be a date, timestamp, year-month, or year");
                }
            }
        }

        if (errors.Count > 0) {
            throw new ArgumentException($"Invalid entity metadata patch: {string.Join("; ", errors)}.");
        }
    }
}

/// <summary>Combines the legacy date dictionary and typed plugin entries into canonical stored codes.</summary>
internal static class EntityMetadataDateNormalization {
    public static IReadOnlyDictionary<string, string> Normalize(EntityMetadataPatch patch) {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (code, value) in patch.Dates ?? new Dictionary<string, string>()) {
            if (!string.IsNullOrWhiteSpace(code)) {
                normalized[EntityDateTypeRegistry.NormalizeCode(code)] = value;
            }
        }

        foreach (var entry in patch.DateEntries ?? []) {
            normalized[entry.Type.ToCode()] = entry.Value;
        }

        return normalized;
    }
}
