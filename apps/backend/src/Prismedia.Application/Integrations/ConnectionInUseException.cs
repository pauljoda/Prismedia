namespace Prismedia.Application.Integrations;

/// <summary>Transfer evidence still refers to this connection, so its identity must be retained.</summary>
public sealed class ConnectionInUseException()
    : Exception("This connection has retained transfers, requests, managed library records, or mapped libraries. Disable it to stop new work while preserving its records and file protection.");
