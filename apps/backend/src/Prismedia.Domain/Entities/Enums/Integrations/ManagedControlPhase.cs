namespace Prismedia.Domain.Entities;

/// <summary>Durable progress of explicit manager settings and search, independent of file availability.</summary>
public enum ManagedControlPhase {
    [Code("pending-configuration")] PendingConfiguration,
    [Code("configuration-uncertain")] ConfigurationUncertain,
    [Code("pending-search")] PendingSearch,
    [Code("search-uncertain")] SearchUncertain,
    [Code("awaiting-command")] AwaitingCommand,
    [Code("completed")] Completed,
    [Code("rejected")] Rejected,
    [Code("failed")] Failed,
    [Code("cancelled")] Cancelled,
    [Code("closed-unverified")] ClosedUnverified
}
