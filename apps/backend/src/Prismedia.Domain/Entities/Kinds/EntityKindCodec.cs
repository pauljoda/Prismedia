namespace Prismedia.Domain.Entities;

/// <summary>
/// Encodes <see cref="EntityKind"/> values from their discovered definitions. This lets each
/// definition own its stable code while retaining the same generic codec API used by persistence,
/// JSON converters, and generated clients.
/// </summary>
public sealed class EntityKindCodec : ICodec<EntityKind> {
    /// <inheritdoc />
    public Type ValueType => typeof(EntityKind);

    /// <inheritdoc />
    public string Encode(EntityKind value) => EntityKindRegistry.Describe(value).Code;

    /// <inheritdoc />
    public EntityKind Decode(string code) =>
        TryDecode(code, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unsupported EntityKind code.");

    /// <inheritdoc />
    public bool TryDecode(string code, out EntityKind value) => EntityKindRegistry.TryGet(code, out value);

    /// <inheritdoc />
    public string EncodeObject(object value) =>
        value is EntityKind kind
            ? Encode(kind)
            : throw new ArgumentException("Expected EntityKind.", nameof(value));

    /// <inheritdoc />
    public object DecodeObject(string code) => Decode(code);
}
