namespace Prismedia.Application.Integrations;

/// <summary>The requested durable transfer no longer exists.</summary>
public sealed class IntegrationTransferNotFoundException() : Exception("The integration transfer was not found.");
