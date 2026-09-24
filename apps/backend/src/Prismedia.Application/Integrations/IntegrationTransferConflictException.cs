namespace Prismedia.Application.Integrations;

/// <summary>The operation key was reused with another intent, ownership is occupied, or the saved revision changed.</summary>
public sealed class IntegrationTransferConflictException(string message) : Exception(message);
