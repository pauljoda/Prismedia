using System.Text.Json;
using System.Text.Json.Serialization;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Serialization;

/// <summary>
/// Serializes any enum that has a registered <see cref="CodecRegistry" /> codec as its stable
/// string code instead of its numeric value, keeping the HTTP wire format identical to the
/// hand-written string codes used before the domain value objects were shared with the contracts.
/// </summary>
public sealed class CodecJsonConverterFactory : JsonConverterFactory {
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsEnum && CodecRegistry.TryGet(typeToConvert, out _);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(CodecJsonConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class CodecJsonConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum {
        private readonly ICodec<TEnum> _codec = CodecRegistry.Get<TEnum>();

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            var code = reader.GetString()
                ?? throw new JsonException($"Expected a string code for {typeof(TEnum).Name}.");
            return _codec.Decode(code);
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteStringValue(_codec.Encode(value));
    }
}
