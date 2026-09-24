namespace Prismedia.Application.Integrations;

/// <summary>The reviewed scope, accepted request, or saved action revision changed.</summary>
public sealed class ManagedControlConflictException(string message) : Exception(message);
