namespace Prismedia.Domain.Entities;

/// <summary>Closed set of comparison operators supported by collection rule conditions.</summary>
public enum CollectionRuleOperator {
    /// <summary>Values are equal.</summary>
    [Code("equals")]
    Equals,

    /// <summary>Values are not equal.</summary>
    [Code("not_equals")]
    NotEquals,

    /// <summary>Text contains a value.</summary>
    [Code("contains")]
    Contains,

    /// <summary>Text does not contain a value.</summary>
    [Code("not_contains")]
    NotContains,

    /// <summary>Value is greater than the supplied value.</summary>
    [Code("greater_than")]
    GreaterThan,

    /// <summary>Value is less than the supplied value.</summary>
    [Code("less_than")]
    LessThan,

    /// <summary>Value is greater than or equal to the supplied value.</summary>
    [Code("greater_equal")]
    GreaterEqual,

    /// <summary>Value is less than or equal to the supplied value.</summary>
    [Code("less_equal")]
    LessEqual,

    /// <summary>Value lies within an inclusive range.</summary>
    [Code("between")]
    Between,

    /// <summary>Value belongs to a supplied set.</summary>
    [Code("in")]
    In,

    /// <summary>Value does not belong to a supplied set.</summary>
    [Code("not_in")]
    NotIn,

    /// <summary>Value is null.</summary>
    [Code("is_null")]
    IsNull,

    /// <summary>Value is not null.</summary>
    [Code("is_not_null")]
    IsNotNull,

    /// <summary>Boolean value is true.</summary>
    [Code("is_true")]
    IsTrue,

    /// <summary>Boolean value is false.</summary>
    [Code("is_false")]
    IsFalse
}
