using System.Collections.Concurrent;
using System.Reflection;

namespace Prismedia.Domain.Entities;

/// <summary>
/// Resolves codecs for closed-set value types. Attribute-backed enums use the shared
/// <see cref="EnumCodec{TValue}"/> automatically; exceptional roots such as
/// <see cref="EntityKind"/> supply one discoverable custom codec whose values come from richer
/// definitions. Neither path requires edits to this registry when a new closed set is introduced.
/// </summary>
public static class CodecRegistry {
    private static readonly ConcurrentDictionary<Type, ICodec> Cache = new();
    private static readonly IReadOnlyDictionary<Type, ICodec> CustomCodecs = DiscoverCustomCodecs();

    /// <summary>
    /// Gets the codec for a code-bearing closed-set value type.
    /// </summary>
    /// <typeparam name="TValue">Value type to encode or decode.</typeparam>
    /// <returns>The codec for the value type.</returns>
    public static ICodec<TValue> Get<TValue>()
        where TValue : struct =>
        (ICodec<TValue>)Cache.GetOrAdd(typeof(TValue), CreateRequired);

    /// <summary>
    /// Attempts to resolve the codec for a runtime value type, succeeding when a custom codec was
    /// discovered or every member declares a <see cref="CodeAttribute"/>.
    /// </summary>
    /// <param name="valueType">Value type to resolve a codec for.</param>
    /// <param name="codec">Resolved codec when the type opts in to codec support.</param>
    /// <returns><see langword="true" /> when a codec is available; otherwise <see langword="false" />.</returns>
    public static bool TryGet(Type valueType, out ICodec? codec) {
        if (CustomCodecs.ContainsKey(valueType) || valueType.IsEnum && IsCodeable(valueType)) {
            codec = Cache.GetOrAdd(valueType, CreateRequired);
            return true;
        }

        codec = null;
        return false;
    }

    private static bool IsCodeable(Type enumType) =>
        enumType.GetFields(BindingFlags.Public | BindingFlags.Static)
            .All(field => field.GetCustomAttribute<CodeAttribute>() is not null);

    private static ICodec CreateRequired(Type valueType) {
        if (CustomCodecs.TryGetValue(valueType, out var customCodec)) {
            return customCodec;
        }

        if (valueType.IsEnum && IsCodeable(valueType)) {
            return (ICodec)Activator.CreateInstance(typeof(EnumCodec<>).MakeGenericType(valueType))!;
        }

        throw new InvalidOperationException($"No codec is available for '{valueType.Name}'.");
    }

    private static IReadOnlyDictionary<Type, ICodec> DiscoverCustomCodecs() {
        var codecType = typeof(ICodec);
        var codecs = codecType.Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } &&
                           codecType.IsAssignableFrom(type))
            .Select(type => Activator.CreateInstance(type, nonPublic: true) as ICodec
                ?? throw new InvalidOperationException(
                    $"Custom codec '{type.FullName}' must have a parameterless constructor."))
            .ToArray();
        var duplicate = codecs.GroupBy(codec => codec.ValueType).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) {
            throw new InvalidOperationException(
                $"Multiple custom codecs were discovered for '{duplicate.Key.Name}'.");
        }

        return codecs.ToDictionary(codec => codec.ValueType);
    }
}
