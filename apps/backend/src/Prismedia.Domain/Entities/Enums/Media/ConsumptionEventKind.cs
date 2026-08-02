namespace Prismedia.Domain.Entities;

/// <summary>Closed set of durable consumption-history event kinds.</summary>
public enum ConsumptionEventKind {
    /// <summary>The entity was opened or started for active consumption.</summary>
    [Code("accessed")]
    Accessed,

    /// <summary>The entity reached an intentional completion event.</summary>
    [Code("completed")]
    Completed,

    /// <summary>The entity was likely abandoned quickly before meaningful activity.</summary>
    [Code("skipped")]
    Skipped
}
