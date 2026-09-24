namespace Prismedia.Application.Integrations;

/// <summary>Current connection configuration has not authorized this operation; retained intent may be retried after configuration
/// recovers.</summary>
public sealed class ConnectionCapabilityUnavailableException() : ArgumentException(
    "This connection does not currently support the requested operation. Check its status and enabled capabilities in Settings.");
