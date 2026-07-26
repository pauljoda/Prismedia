namespace Prismedia.Domain.Entities;

/// <summary>Closed set of durable events that may pause graph expansion without holding a worker.</summary>
public enum JobGraphSignalKind {
    [Code("identify-review")]
    IdentifyReview,

    [Code("external-transfer")]
    ExternalTransfer,

    [Code("domain-event")]
    DomainEvent
}
