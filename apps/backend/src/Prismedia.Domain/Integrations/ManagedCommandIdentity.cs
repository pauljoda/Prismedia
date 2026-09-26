namespace Prismedia.Domain.Integrations;

/// <summary>Exact command identity. Retain its full timestamp precision when persisting this record.</summary>
public sealed record ManagedCommandIdentity(string Id, DateTimeOffset QueuedAt);
