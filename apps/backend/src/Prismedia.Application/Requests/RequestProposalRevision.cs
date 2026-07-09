using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Application.Requests;

/// <summary>Computes stable content revisions for complete plugin metadata proposals.</summary>
public static class RequestProposalRevision {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Returns the lower-case SHA-256 digest of a proposal serialized with object keys in ordinal
    /// order. Array order is preserved because it carries provider ranking and structural meaning.
    /// </summary>
    public static string Compute(EntityMetadataProposal proposal) {
        ArgumentNullException.ThrowIfNull(proposal);

        using var document = JsonSerializer.SerializeToDocument(proposal, JsonOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) {
            WriteCanonical(writer, document.RootElement);
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

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

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
