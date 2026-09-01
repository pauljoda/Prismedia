namespace Prismedia.Domain.Entities;

/// <summary>Determines a node's scheduling urgency and whether its terminal failure fails its owning graph.</summary>
public enum JobNodeImportance {
    [Code("required")]
    Required,

    [Code("best-effort")]
    BestEffort,

    /// <summary>Long-running optional work that yields to ordinary best-effort enrichment.</summary>
    [Code("deferred")]
    Deferred
}
