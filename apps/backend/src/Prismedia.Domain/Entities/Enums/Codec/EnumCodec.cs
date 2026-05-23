using System.Reflection;

namespace Prismedia.Domain.Entities;

/// <summary>
/// The single codec for any enum whose members declare their stable code with
/// <see cref="CodeAttribute"/>. The mapping builds itself from the attributes, so
/// there are no per-enum codec classes and no parallel dictionary to maintain.
/// Construction fails fast if a member is missing a code or two members share one.
/// </summary>
/// <typeparam name="TValue">Closed-set enum type handled by the codec.</typeparam>
public sealed class EnumCodec<TValue> : ICodec<TValue>
    where TValue : struct, Enum {
    private static readonly IReadOnlyDictionary<TValue, string> EncodeMap = BuildEncodeMap();
    private static readonly IReadOnlyDictionary<string, TValue> DecodeMap = BuildDecodeMap();

    /// <inheritdoc />
    public Type ValueType => typeof(TValue);

    /// <inheritdoc />
    public string Encode(TValue value) =>
        EncodeMap.TryGetValue(value, out var code)
            ? code
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unsupported {typeof(TValue).Name} value.");

    /// <inheritdoc />
    public TValue Decode(string code) =>
        TryDecode(code, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(code), code, $"Unsupported {typeof(TValue).Name} code.");

    /// <inheritdoc />
    public bool TryDecode(string code, out TValue value) {
        if (string.IsNullOrWhiteSpace(code)) {
            value = default;
            return false;
        }

        return DecodeMap.TryGetValue(Normalize(code), out value);
    }

    /// <inheritdoc />
    public string EncodeObject(object value) =>
        value is TValue typedValue
            ? Encode(typedValue)
            : throw new ArgumentException($"Expected {typeof(TValue).Name}.", nameof(value));

    /// <inheritdoc />
    public object DecodeObject(string code) => Decode(code);

    /// <summary>
    /// True when every member of <typeparamref name="TValue" /> declares a
    /// <see cref="CodeAttribute"/>, i.e. the enum opts in to codec support.
    /// </summary>
    public static bool IsCodeable() =>
        typeof(TValue).GetFields(BindingFlags.Public | BindingFlags.Static)
            .All(field => field.GetCustomAttribute<CodeAttribute>() is not null);

    private static Dictionary<TValue, string> BuildEncodeMap() {
        var map = new Dictionary<TValue, string>();
        foreach (var field in typeof(TValue).GetFields(BindingFlags.Public | BindingFlags.Static)) {
            var attribute = field.GetCustomAttribute<CodeAttribute>()
                ?? throw new InvalidOperationException(
                    $"Enum member {typeof(TValue).Name}.{field.Name} is missing a [Code] attribute.");
            map[(TValue)field.GetValue(null)!] = attribute.Code;
        }

        return map;
    }

    private static Dictionary<string, TValue> BuildDecodeMap() {
        var map = new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var (value, code) in EncodeMap) {
            var normalized = Normalize(code);
            if (!map.TryAdd(normalized, value)) {
                throw new InvalidOperationException(
                    $"Duplicate code '{code}' declared on {typeof(TValue).Name}.");
            }
        }

        return map;
    }

    private static string Normalize(string code) => code.Trim().ToLowerInvariant();
}
