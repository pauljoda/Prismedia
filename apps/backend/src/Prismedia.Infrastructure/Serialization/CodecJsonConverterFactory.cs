using System.Text.Json;
using System.Text.Json.Serialization;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Serialization;

/// <summary>
/// Serializes any closed-set value type that has a registered <see cref="CodecRegistry" /> codec as its stable
/// string code instead of its numeric value, keeping the JSON wire format identical to the
/// hand-written string codes used before the domain value objects were shared with the contracts.
/// Lives in Infrastructure so every serialization boundary — the HTTP API, the plugin process
/// wire, and the durable identify-queue JSON columns — can round-trip codec enums by code.
/// </summary>
public sealed class CodecJsonConverterFactory : JsonConverterFactory {
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) =>
        CodecRegistry.TryGet(typeToConvert, out _);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(CodecJsonConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class CodecJsonConverter<TValue> : JsonConverter<TValue>
        where TValue : struct {
        private readonly ICodec<TValue> _codec = CodecRegistry.Get<TValue>();

        public override TValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            var code = reader.GetString()
                ?? throw new JsonException($"Expected a string code for {typeof(TValue).Name}.");
            return _codec.Decode(code);
        }

        public override void Write(Utf8JsonWriter writer, TValue value, JsonSerializerOptions options) =>
            writer.WriteStringValue(_codec.Encode(value));
    }
}
