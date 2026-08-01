namespace Prismedia.Domain.Entities;

/// <summary>Outcome of one proposed or applied filesystem organization operation.</summary>
public enum OrganizeItemStatus {
    /// <summary>The operation is ready to apply.</summary>
    [Code("ready")]
    Ready,

    /// <summary>The source already occupies its canonical target path.</summary>
    [Code("unchanged")]
    Unchanged,

    /// <summary>The operation cannot or should not be applied in the current plan.</summary>
    [Code("skipped")]
    Skipped,

    /// <summary>The source and its stored paths were moved successfully.</summary>
    [Code("applied")]
    Applied,

    /// <summary>The attempted operation failed.</summary>
    [Code("failed")]
    Failed
}
