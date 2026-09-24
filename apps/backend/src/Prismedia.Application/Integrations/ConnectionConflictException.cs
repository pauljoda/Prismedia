namespace Prismedia.Application.Integrations;

/// <summary>The caller attempted to replace newer connection configuration or one of its immutable resources.</summary>
public sealed class ConnectionConflictException(string? message = null)
    : Exception(message ?? "The connection changed. Reload it before retrying.");
