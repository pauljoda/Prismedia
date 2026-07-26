namespace Prismedia.Domain.Entities;

/// <summary>Determines whether a terminal node failure fails its owning graph.</summary>
public enum JobNodeImportance {
    [Code("required")]
    Required,

    [Code("best-effort")]
    BestEffort
}
