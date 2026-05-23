using System.Collections.Concurrent;
using System.Reflection;

namespace Prismedia.Domain.Entities;

/// <summary>
/// Resolves the single <see cref="EnumCodec{TValue}"/> for any enum that declares
/// its codes with <see cref="CodeAttribute"/>. There is no reflection-discovered
/// registry of codec classes — the codec builds itself from the enum.
/// </summary>
public static class CodecRegistry {
    private static readonly ConcurrentDictionary<Type, ICodec> Cache = new();

    /// <summary>
    /// Gets the codec for a code-bearing enum type.
    /// </summary>
    /// <typeparam name="TValue">Enum value type to encode or decode.</typeparam>
    /// <returns>The codec for the enum type.</returns>
    public static ICodec<TValue> Get<TValue>()
        where TValue : struct, Enum =>
        (ICodec<TValue>)Cache.GetOrAdd(typeof(TValue), static _ => new EnumCodec<TValue>());

    /// <summary>
    /// Attempts to resolve the codec for a runtime enum type, succeeding only when
    /// the type is an enum whose members all declare a <see cref="CodeAttribute"/>.
    /// </summary>
    /// <param name="valueType">Enum type to resolve a codec for.</param>
    /// <param name="codec">Resolved codec when the type opts in to codec support.</param>
    /// <returns><see langword="true" /> when a codec is available; otherwise <see langword="false" />.</returns>
    public static bool TryGet(Type valueType, out ICodec? codec) {
        if (valueType.IsEnum && IsCodeable(valueType)) {
            codec = Cache.GetOrAdd(valueType, static type =>
                (ICodec)Activator.CreateInstance(typeof(EnumCodec<>).MakeGenericType(type))!);
            return true;
        }

        codec = null;
        return false;
    }

    private static bool IsCodeable(Type enumType) =>
        enumType.GetFields(BindingFlags.Public | BindingFlags.Static)
            .All(field => field.GetCustomAttribute<CodeAttribute>() is not null);
}
