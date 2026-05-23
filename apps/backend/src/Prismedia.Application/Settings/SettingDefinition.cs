using System.Text.Json;
using Prismedia.Contracts.Settings;

namespace Prismedia.Application.Settings;

/// <summary>
/// Primitive value kinds supported by the centralized settings registry.
/// </summary>
public enum SettingValueType {
    Boolean,
    Integer,
    Decimal,
    String,
    StringList,
    Select,
}

/// <summary>
/// Result returned after validating and normalizing a setting value.
/// </summary>
/// <param name="IsValid">Whether the supplied value matched the setting definition.</param>
/// <param name="Value">Normalized value to persist or return.</param>
/// <param name="Error">Human-readable validation failure message.</param>
public sealed record SettingValidationResult(bool IsValid, JsonElement Value, string? Error) {
    /// <summary>
    /// Creates a successful validation result with the normalized value.
    /// </summary>
    public static SettingValidationResult Valid(JsonElement value) => new(true, value, null);

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static SettingValidationResult Invalid(string error) => new(false, default, error);
}

/// <summary>
/// Registry entry that defines one app-global setting, including validation and UI metadata.
/// </summary>
public sealed class SettingDefinition {
    public SettingDefinition(
        string key,
        string groupKey,
        string groupLabel,
        string groupDescription,
        int groupOrder,
        string label,
        string description,
        SettingValueType type,
        JsonElement defaultValue,
        int order,
        SettingConstraints? constraints = null,
        IReadOnlyList<SettingOption>? options = null,
        string? inputKind = null,
        string? applyHint = null,
        bool emptyStringUsesDefault = false) {
        Key = key;
        GroupKey = groupKey;
        GroupLabel = groupLabel;
        GroupDescription = groupDescription;
        GroupOrder = groupOrder;
        Label = label;
        Description = description;
        Type = type;
        DefaultValue = defaultValue.Clone();
        Order = order;
        Constraints = constraints;
        Options = options ?? [];
        InputKind = inputKind;
        ApplyHint = applyHint;
        EmptyStringUsesDefault = emptyStringUsesDefault;
    }

    /// <summary>Stable dotted key used by API clients and persisted overrides.</summary>
    public string Key { get; }

    /// <summary>Stable key for the display group that owns this setting.</summary>
    public string GroupKey { get; }

    /// <summary>Human-readable group label.</summary>
    public string GroupLabel { get; }

    /// <summary>Short description for the display group.</summary>
    public string GroupDescription { get; }

    /// <summary>Display order of the group.</summary>
    public int GroupOrder { get; }

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

    /// <summary>
    /// Validates a raw JSON value and returns the normalized value that should be persisted.
    /// </summary>
    /// <param name="value">Raw incoming JSON value.</param>
    public SettingValidationResult Validate(JsonElement value) {
        return Type switch {
            SettingValueType.Boolean => ValidateBoolean(value),
            SettingValueType.Integer => ValidateInteger(value),
            SettingValueType.Decimal => ValidateDecimal(value),
            SettingValueType.String => ValidateString(value),
            SettingValueType.StringList => ValidateStringList(value),
            SettingValueType.Select => ValidateSelect(value),
            _ => SettingValidationResult.Invalid($"Unsupported setting type '{Type}'.")
        };
    }

    /// <summary>
    /// Converts the definition and effective value to an API descriptor.
    /// </summary>
    /// <param name="value">Effective setting value.</param>
    /// <param name="isDefault">Whether the effective value came from the registry default.</param>
    public SettingDescriptor ToDescriptor(JsonElement value, bool isDefault) =>
        new(
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

    private SettingValidationResult ValidateBoolean(JsonElement value) =>
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? SettingValidationResult.Valid(JsonSerializer.SerializeToElement(value.GetBoolean()))
            : SettingValidationResult.Invalid($"{Key} must be a boolean.");

    private SettingValidationResult ValidateInteger(JsonElement value) {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number)) {
            return SettingValidationResult.Invalid($"{Key} must be an integer.");
        }

        if (Constraints?.Min is { } min && number < min || Constraints?.Max is { } max && number > max) {
            return SettingValidationResult.Invalid(
                $"{Key} must be between {Constraints?.Min:0} and {Constraints?.Max:0}.");
        }

        return SettingValidationResult.Valid(JsonSerializer.SerializeToElement(number));
    }

    private SettingValidationResult ValidateDecimal(JsonElement value) {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number)) {
            return SettingValidationResult.Invalid($"{Key} must be a decimal number.");
        }

        if (Constraints?.Min is { } min && number < min || Constraints?.Max is { } max && number > max) {
            return SettingValidationResult.Invalid($"{Key} must be between {Constraints?.Min} and {Constraints?.Max}.");
        }

        return SettingValidationResult.Valid(JsonSerializer.SerializeToElement(number));
    }

    private SettingValidationResult ValidateString(JsonElement value) {
        if (value.ValueKind != JsonValueKind.String) {
            return SettingValidationResult.Invalid($"{Key} must be a string.");
        }

        var text = value.GetString()?.Trim() ?? string.Empty;
        if (EmptyStringUsesDefault && string.IsNullOrEmpty(text)) {
            return SettingValidationResult.Valid(DefaultValue.Clone());
        }

        return SettingValidationResult.Valid(JsonSerializer.SerializeToElement(text));
    }

    private SettingValidationResult ValidateStringList(JsonElement value) {
        string[] items;
        if (value.ValueKind == JsonValueKind.String) {
            items = (value.GetString() ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        } else if (value.ValueKind == JsonValueKind.Array) {
            var result = new List<string>();
            foreach (var item in value.EnumerateArray()) {
                if (item.ValueKind != JsonValueKind.String) {
                    return SettingValidationResult.Invalid($"{Key} must be a list of strings.");
                }

                var text = item.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text)) {
                    result.Add(text);
                }
            }

            items = result.ToArray();
        } else {
            return SettingValidationResult.Invalid($"{Key} must be a list of strings.");
        }

        if (Constraints?.MinItems is { } minItems && items.Length < minItems) {
            return SettingValidationResult.Invalid($"{Key} must include at least {minItems} item.");
        }

        if (Constraints?.MaxItems is { } maxItems && items.Length > maxItems) {
            return SettingValidationResult.Invalid($"{Key} must include no more than {maxItems} items.");
        }

        return SettingValidationResult.Valid(JsonSerializer.SerializeToElement(items));
    }

    private SettingValidationResult ValidateSelect(JsonElement value) {
        if (value.ValueKind != JsonValueKind.String) {
            return SettingValidationResult.Invalid($"{Key} must be a string option.");
        }

        var selected = value.GetString()?.Trim() ?? string.Empty;
        if (!Options.Any(option => string.Equals(option.Value, selected, StringComparison.Ordinal))) {
            return SettingValidationResult.Invalid($"{Key} must be one of: {string.Join(", ", Options.Select(o => o.Value))}.");
        }

        return SettingValidationResult.Valid(JsonSerializer.SerializeToElement(selected));
    }

    private static string ToWireType(SettingValueType type) =>
        type switch {
            SettingValueType.Boolean => "boolean",
            SettingValueType.Integer => "integer",
            SettingValueType.Decimal => "decimal",
            SettingValueType.String => "string",
            SettingValueType.StringList => "stringList",
            SettingValueType.Select => "select",
            _ => "unknown"
        };
}
