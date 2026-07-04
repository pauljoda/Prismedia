using System.Text.Json;
using Prismedia.Application.Acquisition;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Serializes and deserializes a custom format's conditions to/from the durable <c>conditions_json</c>
/// column. The <see cref="CustomFormatConditionType"/> discriminator is a <c>[Code]</c> enum, so the
/// <see cref="CodecJsonConverterFactory"/> is included to round-trip it as its stable string code (not its
/// numeric value), keeping the stored blob resilient to enum reordering — the same rule the identify-queue
/// JSON columns follow.
/// </summary>
internal static class CustomFormatConditionsJson {
    private static readonly JsonSerializerOptions Options = new() {
        Converters = { new CodecJsonConverterFactory() }
    };

    /// <summary>Serializes the conditions for storage.</summary>
    public static string Serialize(IReadOnlyList<CustomFormatCondition> conditions) =>
        JsonSerializer.Serialize(conditions, Options);

    /// <summary>Deserializes stored conditions; a malformed blob yields no conditions (the format then never matches) rather than throwing.</summary>
    public static IReadOnlyList<CustomFormatCondition> Deserialize(string json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return [];
        }

        try {
            return JsonSerializer.Deserialize<CustomFormatCondition[]>(json, Options) ?? [];
        } catch (JsonException) {
            return [];
        }
    }
}
