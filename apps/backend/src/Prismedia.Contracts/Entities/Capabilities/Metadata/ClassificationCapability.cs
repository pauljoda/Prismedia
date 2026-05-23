namespace Prismedia.Contracts.Entities;

/// <summary>API-facing provider or user classification capability.</summary>
[CapabilityKind("classification")]
public sealed record ClassificationCapability(string? Value, string? System) : EntityCapability;
