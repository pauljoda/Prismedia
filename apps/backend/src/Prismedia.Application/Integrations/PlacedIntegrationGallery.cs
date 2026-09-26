namespace Prismedia.Application.Integrations;

/// <summary>One atomically published gallery folder and its exact artifact-to-file mapping.</summary>
public sealed record PlacedIntegrationGallery(string FolderPath, IReadOnlyDictionary<string, string> Files);
