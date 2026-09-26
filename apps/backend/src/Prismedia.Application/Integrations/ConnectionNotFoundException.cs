namespace Prismedia.Application.Integrations;

/// <summary>The selected connection no longer exists.</summary>
public sealed class ConnectionNotFoundException() : Exception("The connection was not found.");
