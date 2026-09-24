namespace Prismedia.Infrastructure.Integrations;

/// <summary>Private application work areas that must never become an externally managed library.</summary>
public sealed record ExternalLibraryStorageOptions(string DataPath, string CachePath);
