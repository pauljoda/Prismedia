namespace Prismedia.Domain.Entities;

/// <summary>
/// Extension helpers for encoding and decoding enum values through the discovered codec registry.
/// </summary>
public static class CodecRegistryExtensions {
    /// <summary>
    /// Encodes a closed-set enum value with its discovered codec.
    /// </summary>
    /// <typeparam name="TValue">Enum value type to encode.</typeparam>
    /// <param name="value">Enum value to encode.</param>
    /// <returns>Stable text code used by database rows and HTTP contracts.</returns>
    public static string ToCode<TValue>(this TValue value)
        where TValue : struct, Enum =>
        CodecRegistry.Get<TValue>().Encode(value);

    /// <summary>
    /// Decodes a stable text code with the codec registered for <typeparamref name="TValue" />.
    /// </summary>
    /// <typeparam name="TValue">Enum value type to decode.</typeparam>
    /// <param name="code">Text code from storage or API input.</param>
    /// <returns>Enum value represented by the code.</returns>
    public static TValue DecodeAs<TValue>(this string code)
        where TValue : struct, Enum =>
        CodecRegistry.Get<TValue>().Decode(code);

    /// <summary>
    /// Attempts to decode a stable text code with the codec registered for <typeparamref name="TValue" />.
    /// </summary>
    /// <typeparam name="TValue">Enum value type to decode.</typeparam>
    /// <param name="code">Text code from storage or API input.</param>
    /// <param name="value">Decoded enum value when the code is known.</param>
    /// <returns><see langword="true" /> when the code was recognized; otherwise <see langword="false" />.</returns>
    public static bool TryDecodeAs<TValue>(this string code, out TValue value)
        where TValue : struct, Enum =>
        CodecRegistry.Get<TValue>().TryDecode(code, out value);
}
