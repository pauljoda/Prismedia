namespace Prismedia.Application.Integrations;

/// <summary>Durable local evidence of exact bytes in private staging. The path is never a provider-chosen destination.</summary>
public sealed record VerifiedIntegrationArtifact(string ArtifactId, string Path, long SizeBytes, string Sha256, string FileName);
