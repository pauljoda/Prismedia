namespace Prismedia.Domain.Entities;

/// <summary>
/// Closed set of strategies for projecting a plugin proposal into selectable request-review targets.
/// </summary>
public enum RequestReviewSelection {
    /// <summary>The proposal root is the request target.</summary>
    [Code("root")]
    Root,

    /// <summary>The proposal's direct structural children are request targets.</summary>
    [Code("direct-children")]
    DirectChildren,

    /// <summary>Direct children are targets when present; otherwise the proposal root is targeted.</summary>
    [Code("direct-children-when-present")]
    DirectChildrenWhenPresent
}
