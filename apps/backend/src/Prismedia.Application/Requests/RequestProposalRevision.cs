using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>Computes stable content revisions for complete plugin metadata proposals.</summary>
public static class RequestProposalRevision {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        Converters = {
            new EntityKindRevisionConverter(),
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    /// <summary>
    /// Returns the lower-case SHA-256 digest of a proposal serialized with object keys in ordinal
    /// order and decimals in their shortest equivalent form. Array order is preserved because it
    /// carries provider ranking and structural meaning.
    /// </summary>
    public static string Compute(EntityMetadataProposal proposal) {
        ArgumentNullException.ThrowIfNull(proposal);

        using var document = JsonSerializer.SerializeToDocument(WithoutSyntheticEntityIds(proposal), JsonOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) {
            WriteCanonical(writer, document.RootElement);
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static EntityMetadataProposal WithoutSyntheticEntityIds(EntityMetadataProposal proposal) =>
        proposal with {
            TargetEntityId = null,
            Children = (proposal.Children ?? []).Select(WithoutSyntheticEntityIds).ToArray(),
            Relationships = (proposal.Relationships ?? []).Select(WithoutSyntheticEntityIds).ToArray()
        };

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element) {
        switch (element.ValueKind) {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal)) {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.Number:
                if (element.TryGetDecimal(out var number)) {
                    writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
                } else {
                    element.WriteTo(writer);
                }
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private sealed class EntityKindRevisionConverter : JsonConverter<EntityKind> {
        public override EntityKind Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) {
            var value = reader.GetString() ?? throw new JsonException("Expected an EntityKind name.");
            if (Enum.TryParse<EntityKind>(value, ignoreCase: true, out var entityKind) ||
                EntityKindRegistry.TryGet(value, out entityKind)) {
                return entityKind;
            }

            throw new JsonException($"Unsupported EntityKind revision name '{value}'.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            EntityKind value,
            JsonSerializerOptions options) {
            writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
        }
    }
}
