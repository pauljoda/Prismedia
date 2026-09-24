namespace Prismedia.Application.Integrations;

/// <summary>Whether exact local bytes were attached; a waiting reason never claims import success.</summary>
public sealed record ManagedRequestMaterialization(bool Imported, string? WaitingReason = null);
