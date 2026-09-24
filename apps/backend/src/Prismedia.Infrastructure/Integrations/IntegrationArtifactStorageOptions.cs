namespace Prismedia.Infrastructure.Integrations;

/// <summary>Private artifact staging location shared by API and worker.</summary>
public sealed record IntegrationArtifactStorageOptions(string RootPath);
