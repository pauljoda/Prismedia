namespace Prismedia.Domain.Integrations;

/// <summary>Retained requested content identity, independent of whether its first source file exists yet.</summary>
public sealed record ManagedTargetBinding(ManagedTargetIdentity Target, Guid EntityId);
