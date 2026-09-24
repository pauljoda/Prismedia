namespace Prismedia.Application.Integrations;

/// <summary>Persistent encryption keys are unavailable; stored secrets must be recovered or replaced.</summary>
public sealed class ConnectionSecretUnavailableException()
    : Exception("Connection credentials could not be decrypted. Restore the key directory or enter the credentials again.");
