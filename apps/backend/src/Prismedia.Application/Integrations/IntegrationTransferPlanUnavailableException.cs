namespace Prismedia.Application.Integrations;

/// <summary>The persistent key ring cannot decrypt an already accepted transfer intent.</summary>
public sealed class IntegrationTransferPlanUnavailableException()
    : Exception("The saved transfer intent could not be decrypted. Restore the persistent encryption keys before retrying.");
