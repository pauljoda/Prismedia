namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of input controls a plugin can request for an identify search. Values remain simple
/// JSON form scalars; the field type tells Prismedia how to collect and validate those values.
/// </summary>
public enum PluginSearchFieldType {
    /// <summary>Free-form text input.</summary>
    [Code("text")]
    Text,

    /// <summary>General numeric input.</summary>
    [Code("number")]
    Number,

    /// <summary>Four-digit calendar year input.</summary>
    [Code("year")]
    Year
}
