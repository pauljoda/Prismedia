namespace Prismedia.Domain.Entities;

/// <summary>
/// Non-generic contract for a codec that translates one closed-set value type to and from stable text codes.
/// </summary>
public interface ICodec {
    /// <summary>
    /// Gets the closed-set value type handled by this codec.
    /// </summary>
    Type ValueType { get; }

    /// <summary>All stable codes accepted by this codec in canonical order.</summary>
    IReadOnlyList<string> Codes { get; }

    /// <summary>
    /// Encodes a closed-set value boxed as an object.
    /// </summary>
    /// <param name="value">Value supported by this codec.</param>
    /// <returns>Stable text code used by database rows and HTTP contracts.</returns>
    string EncodeObject(object value);

    /// <summary>
    /// Decodes a stable text code into a boxed closed-set value.
    /// </summary>
    /// <param name="code">Text code from storage or API input.</param>
    /// <returns>Enum value represented by the code.</returns>
    object DecodeObject(string code);
}

/// <summary>
/// Type-safe contract for a codec that translates one closed-set value type to and from stable text codes.
/// </summary>
/// <typeparam name="TValue">Closed-set value type handled by this codec.</typeparam>
public interface ICodec<TValue> : ICodec
    where TValue : struct {
    /// <summary>
    /// Encodes a value into its stable text code.
    /// </summary>
    /// <param name="value">Enum value to encode.</param>
    /// <returns>Stable text code used by database rows and HTTP contracts.</returns>
    string Encode(TValue value);

    /// <summary>
    /// Decodes a stable text code into a value.
    /// </summary>
    /// <param name="code">Text code from storage or API input.</param>
    /// <returns>Enum value represented by the code.</returns>
    TValue Decode(string code);

    /// <summary>
    /// Attempts to decode a stable text code without throwing.
    /// </summary>
    /// <param name="code">Text code from storage or API input.</param>
    /// <param name="value">Decoded value when the code is known.</param>
    /// <returns><see langword="true" /> when the code was recognized; otherwise <see langword="false" />.</returns>
    bool TryDecode(string code, out TValue value);
}
