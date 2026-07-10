using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Serialization;

/// <summary>
/// Emits codec-backed enums as typed string enums in the OpenAPI document. Codec enums
/// serialize as their stable string code (see <see cref="CodecJsonConverterFactory"/>), so the
/// generator never produces a schema for the enum itself — it collapses each occurrence to a
/// bare <c>string</c>, and the generated frontend models lose all type information. This
/// transformer therefore patches enums in at the owning object level: for every property (or
/// collection item) whose CLR type is a codec enum, it writes <c>enum: [&lt;codes&gt;]</c> onto
/// that property's schema so the generated client emits typed unions that stay in sync with the
/// backend <c>[Code]</c> definitions — the type source that complements the value source in
/// <c>codes.ts</c>.
/// </summary>
internal sealed class CodecEnumSchemaTransformer : IOpenApiSchemaTransformer {
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken) {
        if (schema.Properties is { Count: > 0 }) {
            foreach (var property in context.JsonTypeInfo.Properties) {
                if (schema.Properties.TryGetValue(property.Name, out var propertySchema)
                    && propertySchema is OpenApiSchema concrete) {
                    ApplyCodecEnum(concrete, property.PropertyType);
                }
            }
        }

        return Task.CompletedTask;
    }

    private static void ApplyCodecEnum(OpenApiSchema schema, Type propertyType) {
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (TryCodes(type, out var codes)) {
            schema.Enum = codes;
            return;
        }

        // Arrays / lists of a codec enum: patch the item schema instead.
        var element = ElementType(type);
        if (element is not null && TryCodes(element, out var itemCodes)) {
            var itemSchema = schema.Items as OpenApiSchema ?? new OpenApiSchema();
            itemSchema.Type = JsonSchemaType.String;
            itemSchema.Enum = itemCodes;
            schema.Items = itemSchema;
        }
    }

    private static bool TryCodes(Type type, out List<JsonNode> codes) {
        if (type.IsEnum && CodecRegistry.TryGet(type, out var codec) && codec is not null) {
            codes = Enum.GetValues(type)
                .Cast<object>()
                .Select(value => (JsonNode)JsonValue.Create(codec.EncodeObject(value))!)
                .ToList();
            return true;
        }

        codes = [];
        return false;
    }

    private static Type? ElementType(Type type) {
        if (type.IsArray) {
            return type.GetElementType();
        }

        foreach (var iface in type.GetInterfaces()) {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
