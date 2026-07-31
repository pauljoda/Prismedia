namespace Prismedia.Domain.Entities;

/// <summary>Closed set of boolean operators supported by collection rule groups.</summary>
public enum CollectionRuleGroupOperator {
    /// <summary>Every child rule must match.</summary>
    [Code("and")]
    And,

    /// <summary>At least one child rule must match.</summary>
    [Code("or")]
    Or,

    /// <summary>The conjunction of child rules must not match.</summary>
    [Code("not")]
    Not
}
