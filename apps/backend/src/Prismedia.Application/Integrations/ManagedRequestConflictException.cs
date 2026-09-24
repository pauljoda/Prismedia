namespace Prismedia.Application.Integrations;

/// <summary>The accepted manager intent or its local ownership boundary changed.</summary>
public sealed class ManagedRequestConflictException(string message) : Exception(message);
