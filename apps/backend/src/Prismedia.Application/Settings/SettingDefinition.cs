using System.Text.Json;
using Prismedia.Contracts.Settings;

namespace Prismedia.Application.Settings;

/// <summary>Primitive value kinds supported by the settings catalog.</summary>
public enum SettingValueType {
    Boolean,
    Integer,
    Decimal,
    String,
    StringList,
    StringMap,
    WeightedTermList,
    Select
}

/// <summary>Result returned after validating and normalizing a setting value.</summary>
/// <param name="IsValid">Whether the supplied value matched the setting definition.</param>
/// <param name="Value">Normalized value to persist or return.</param>
/// <param name="Error">Human-readable validation failure message.</param>
public sealed record SettingValidationResult(bool IsValid, JsonElement Value, string? Error) {
    /// <summary>Creates a successful validation result.</summary>
    public static SettingValidationResult Valid(JsonElement value) => new(true, value, null);

    /// <summary>Creates a failed validation result.</summary>
    public static SettingValidationResult Invalid(string error) => new(false, default, error);
}

/// <summary>Display metadata shared by settings in one catalog group.</summary>
/// <param name="Key">Stable group key.</param>
/// <param name="Label">Human-readable group label.</param>
/// <param name="Description">Short group description.</param>
/// <param name="Order">Display order within the settings catalog.</param>
public sealed record SettingGroupDefinition(string Key, string Label, string Description, int Order);

/// <summary>Untyped base class for a discovered application setting.</summary>
public abstract class SettingDefinition {
    private readonly ISettingValueCodec _codec;

    private protected SettingDefinition(
        string key,
        SettingGroupDefinition group,
        string label,
        string description,
        SettingValueType type,
        JsonElement defaultValue,
        int order,
        ISettingValueCodec codec,
        SettingConstraints? constraints = null,
        IReadOnlyList<SettingOption>? options = null,
        string? inputKind = null,
        string? applyHint = null,
        bool emptyStringUsesDefault = false,
        IReadOnlyList<string>? allowedKeys = null) {
        Key = key;
        Group = group;
        Label = label;
        Description = description;
        Type = type;
        DefaultValue = defaultValue.Clone();
        Order = order;
        _codec = codec;
        Constraints = constraints;
        Options = options ?? [];
        InputKind = inputKind;
        ApplyHint = applyHint;
        EmptyStringUsesDefault = emptyStringUsesDefault;
        AllowedKeys = allowedKeys ?? [];
    }

    /// <summary>Stable dotted key used by API clients and persisted overrides.</summary>
    public string Key { get; }

    /// <summary>Display group that owns this setting.</summary>
    public SettingGroupDefinition Group { get; }

    /// <summary>Stable key for the display group that owns this setting.</summary>
    public string GroupKey => Group.Key;

    /// <summary>Human-readable group label.</summary>
    public string GroupLabel => Group.Label;

    /// <summary>Short description for the display group.</summary>
    public string GroupDescription => Group.Description;

    /// <summary>Display order of the group.</summary>
    public int GroupOrder => Group.Order;

    /// <summary>Human-readable setting label.</summary>
    public string Label { get; }

    /// <summary>Short setting description.</summary>
    public string Description { get; }

    /// <summary>Primitive value kind.</summary>
    public SettingValueType Type { get; }

    /// <summary>Default value used when no override is stored.</summary>
    public JsonElement DefaultValue { get; }

    /// <summary>Display order inside the group.</summary>
    public int Order { get; }

    /// <summary>Numeric or collection constraints.</summary>
    public SettingConstraints? Constraints { get; }

    /// <summary>Allowed options for select settings.</summary>
    public IReadOnlyList<SettingOption> Options { get; }

    /// <summary>Optional UI hint, such as path or textarea.</summary>
    public string? InputKind { get; }

    /// <summary>Optional note about when the setting takes effect.</summary>
    public string? ApplyHint { get; }

    /// <summary>Whether an empty string should normalize to the setting default.</summary>
    public bool EmptyStringUsesDefault { get; }

    /// <summary>Optional canonical keys accepted by object-valued settings.</summary>
    public IReadOnlyList<string> AllowedKeys { get; }

    /// <summary>Validates a raw JSON value and returns the normalized value to persist.</summary>
    public SettingValidationResult Validate(JsonElement value) => _codec.Validate(this, value);

    /// <summary>Converts the definition and effective value to an API descriptor.</summary>
    public SettingDescriptor ToDescriptor(JsonElement value, bool isDefault) => new(
        Key,
        GroupKey,
        Label,
        Description,
        ToWireType(Type),
        value.Clone(),
        DefaultValue.Clone(),
        isDefault,
        Order,
        Constraints,
        Options,
        InputKind,
        ApplyHint);

    internal object ReadUntyped(JsonElement value) => _codec.Read(value);

    private static string ToWireType(SettingValueType type) => type switch {
        SettingValueType.Boolean => "boolean",
        SettingValueType.Integer => "integer",
        SettingValueType.Decimal => "decimal",
        SettingValueType.String => "string",
        SettingValueType.StringList => "stringList",
        SettingValueType.StringMap => "stringMap",
        SettingValueType.WeightedTermList => "weightedTermList",
        SettingValueType.Select => "select",
        _ => "unknown"
    };
}

/// <summary>A setting definition with a canonical decoded value type.</summary>
public sealed class SettingDefinition<T> : SettingDefinition {
    internal SettingDefinition(
        string key,
        SettingGroupDefinition group,
        string label,
        string description,
        SettingValueType type,
        T defaultValue,
        int order,
        ISettingValueCodec codec,
        SettingConstraints? constraints = null,
        IReadOnlyList<SettingOption>? options = null,
        string? inputKind = null,
        string? applyHint = null,
        bool emptyStringUsesDefault = false,
        IReadOnlyList<string>? allowedKeys = null)
        : base(
            key,
            group,
            label,
            description,
            type,
            JsonSerializer.SerializeToElement(defaultValue),
            order,
            codec,
            constraints,
            options,
            inputKind,
            applyHint,
            emptyStringUsesDefault,
            allowedKeys) { }

    /// <summary>Decodes an already validated effective JSON value.</summary>
    public T Read(JsonElement value) => (T)ReadUntyped(value);
}

internal interface ISettingValueCodec {
    SettingValidationResult Validate(SettingDefinition definition, JsonElement value);
    object Read(JsonElement value);
}

internal static class SettingValueCodecs {
    public static ISettingValueCodec Boolean { get; } = new DelegateCodec(
        (d, v) => v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? SettingValidationResult.Valid(JsonSerializer.SerializeToElement(v.GetBoolean()))
            : SettingValidationResult.Invalid($"{d.Key} must be a boolean."),
        v => v.GetBoolean());

    public static ISettingValueCodec Integer { get; } = new DelegateCodec((d, v) => {
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var number)) {
            return SettingValidationResult.Invalid($"{d.Key} must be an integer.");
        }

        if (d.Constraints?.Min is { } min && number < min ||
            d.Constraints?.Max is { } max && number > max) {
            return SettingValidationResult.Invalid(
                $"{d.Key} must be between {d.Constraints?.Min:0} and {d.Constraints?.Max:0}.");
        }

        return SettingValidationResult.Valid(JsonSerializer.SerializeToElement(number));
    }, v => v.GetInt32());

    public static ISettingValueCodec Decimal { get; } = new DelegateCodec((d, v) => {
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDecimal(out var number)) {
            return SettingValidationResult.Invalid($"{d.Key} must be a decimal number.");
        }

        if (d.Constraints?.Min is { } min && number < min ||
            d.Constraints?.Max is { } max && number > max) {
            return SettingValidationResult.Invalid(
                $"{d.Key} must be between {d.Constraints?.Min} and {d.Constraints?.Max}.");
        }

        return SettingValidationResult.Valid(JsonSerializer.SerializeToElement(number));
    }, v => v.GetDecimal());

    public static ISettingValueCodec String { get; } = new DelegateCodec((d, v) => {
        if (v.ValueKind != JsonValueKind.String) {
            return SettingValidationResult.Invalid($"{d.Key} must be a string.");
        }

        var text = v.GetString()?.Trim() ?? string.Empty;
        return d.EmptyStringUsesDefault && text.Length == 0
            ? SettingValidationResult.Valid(d.DefaultValue.Clone())
            : SettingValidationResult.Valid(JsonSerializer.SerializeToElement(text));
    }, v => v.GetString() ?? string.Empty);

    public static ISettingValueCodec StringList { get; } = new DelegateCodec((d, v) => {
        string[] items;
        if (v.ValueKind == JsonValueKind.String) {
            items = (v.GetString() ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        } else if (v.ValueKind == JsonValueKind.Array) {
            var result = new List<string>();
            foreach (var item in v.EnumerateArray()) {
                if (item.ValueKind != JsonValueKind.String) {
                    return SettingValidationResult.Invalid($"{d.Key} must be a list of strings.");
                }

                var text = item.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text)) {
                    result.Add(text);
                }
            }

            items = result.ToArray();
        } else {
            return SettingValidationResult.Invalid($"{d.Key} must be a list of strings.");
        }

        if (d.Constraints?.MinItems is { } min && items.Length < min) {
            return SettingValidationResult.Invalid($"{d.Key} must include at least {min} item.");
        }

        if (d.Constraints?.MaxItems is { } max && items.Length > max) {
            return SettingValidationResult.Invalid($"{d.Key} must include no more than {max} items.");
        }

        return SettingValidationResult.Valid(JsonSerializer.SerializeToElement(items));
    }, v => (IReadOnlyList<string>)(v.Deserialize<string[]>() ?? []));

    public static ISettingValueCodec StringMap { get; } = new DelegateCodec((d, v) => {
        if (v.ValueKind != JsonValueKind.Object) {
            return SettingValidationResult.Invalid($"{d.Key} must be an object with string values.");
        }

        var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in v.EnumerateObject()) {
            var canonical = d.AllowedKeys.FirstOrDefault(allowed =>
                allowed.Equals(property.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (d.AllowedKeys.Count > 0 && canonical is null) {
                return SettingValidationResult.Invalid(
                    $"{d.Key} contains unknown key '{property.Name}'.");
            }

            canonical ??= property.Name.Trim();
            if (string.IsNullOrWhiteSpace(canonical) ||
                property.Value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(property.Value.GetString())) {
                return SettingValidationResult.Invalid($"{d.Key} must contain non-empty string keys and values.");
            }

            if (!normalized.TryAdd(canonical, property.Value.GetString()!.Trim())) {
                return SettingValidationResult.Invalid(
                    $"{d.Key} cannot contain duplicate key '{canonical}'.");
            }
        }

        if (d.Constraints?.MinItems is { } min && normalized.Count < min) {
            return SettingValidationResult.Invalid($"{d.Key} must include at least {min} item.");
        }

        if (d.Constraints?.MaxItems is { } max && normalized.Count > max) {
            return SettingValidationResult.Invalid($"{d.Key} must include no more than {max} items.");
        }

        return SettingValidationResult.Valid(JsonSerializer.SerializeToElement(normalized));
    }, v =>
        (IReadOnlyDictionary<string, string>)(
            v.Deserialize<SortedDictionary<string, string>>() ??
            new SortedDictionary<string, string>(StringComparer.Ordinal)));

    public static ISettingValueCodec WeightedTermList { get; } = new DelegateCodec(
        ValidateWeightedTerms,
        v => (IReadOnlyList<SubtitlePreferenceTerm>)(v.Deserialize<SubtitlePreferenceTerm[]>() ?? []));

    public static ISettingValueCodec Select { get; } = new DelegateCodec((d, v) => {
        if (v.ValueKind != JsonValueKind.String) {
            return SettingValidationResult.Invalid($"{d.Key} must be a string option.");
        }

        var selected = v.GetString()?.Trim() ?? string.Empty;
        return d.Options.Any(option => string.Equals(option.Value, selected, StringComparison.Ordinal))
            ? SettingValidationResult.Valid(JsonSerializer.SerializeToElement(selected))
            : SettingValidationResult.Invalid(
                $"{d.Key} must be one of: {string.Join(", ", d.Options.Select(o => o.Value))}.");
    }, v => v.GetString() ?? string.Empty);

    private static SettingValidationResult ValidateWeightedTerms(SettingDefinition d, JsonElement value) {
        if (value.ValueKind == JsonValueKind.String) {
            return Normalize(d, ToLegacyTerms(
                (value.GetString() ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        }

        if (value.ValueKind != JsonValueKind.Array) {
            return SettingValidationResult.Invalid($"{d.Key} must be a list of weighted terms.");
        }

        var items = value.EnumerateArray().ToArray();
        if (items.All(item => item.ValueKind == JsonValueKind.String)) {
            return Normalize(d, ToLegacyTerms(items.Select(item => item.GetString() ?? string.Empty)));
        }

        if (items.Any(item => item.ValueKind != JsonValueKind.Object)) {
            return SettingValidationResult.Invalid($"{d.Key} must be a list of weighted terms.");
        }

        var terms = new List<SubtitlePreferenceTerm>(items.Length);
        foreach (var item in items) {
            if (!item.TryGetProperty("term", out var termElement) ||
                termElement.ValueKind != JsonValueKind.String) {
                return SettingValidationResult.Invalid($"{d.Key} terms must include text.");
            }

            if (!item.TryGetProperty("weight", out var weightElement) ||
                weightElement.ValueKind != JsonValueKind.Number ||
                !weightElement.TryGetInt32(out var weight)) {
                return SettingValidationResult.Invalid($"{d.Key} term weights must be integers.");
            }

            var term = termElement.GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(term)) {
                return SettingValidationResult.Invalid($"{d.Key} terms cannot be blank.");
            }

            var min = (int)(d.Constraints?.Min ?? 1);
            var max = (int)(d.Constraints?.Max ?? 100);
            if (weight < min || weight > max) {
                return SettingValidationResult.Invalid(
                    $"{d.Key} term weights must be between {min} and {max}.");
            }

            terms.Add(new SubtitlePreferenceTerm(term, weight));
        }

        return Normalize(d, terms);
    }

    private static IReadOnlyList<SubtitlePreferenceTerm> ToLegacyTerms(IEnumerable<string> values) =>
        values
            .Select(term => term.Trim())
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select((term, index) => new SubtitlePreferenceTerm(term, Math.Max(1, 100 - index)))
            .ToArray();

    private static SettingValidationResult Normalize(SettingDefinition d, IReadOnlyList<SubtitlePreferenceTerm> terms) {
        if (d.Constraints?.MinItems is { } min && terms.Count < min) {
            return SettingValidationResult.Invalid($"{d.Key} must include at least {min} term.");
        }

        if (d.Constraints?.MaxItems is { } max && terms.Count > max) {
            return SettingValidationResult.Invalid($"{d.Key} must include no more than {max} terms.");
        }

        if (terms.Select(term => term.Term).Distinct(StringComparer.OrdinalIgnoreCase).Count() != terms.Count) {
            return SettingValidationResult.Invalid($"{d.Key} cannot include duplicate terms.");
        }

        return SettingValidationResult.Valid(JsonSerializer.SerializeToElement(terms));
    }

    private sealed class DelegateCodec(
        Func<SettingDefinition, JsonElement, SettingValidationResult> validate,
        Func<JsonElement, object> read) : ISettingValueCodec {
        public SettingValidationResult Validate(SettingDefinition definition, JsonElement value) =>
            validate(definition, value);

        public object Read(JsonElement value) => read(value);
    }
}
