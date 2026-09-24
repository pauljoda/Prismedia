namespace Prismedia.Domain.Integrations;

/// <summary>Exact local source owners for one artifact, plus an optional containing gallery used as its library entrypoint.</summary>
public sealed record IntegrationArtifactImport(string ArtifactId, string Sha256, IReadOnlyList<Guid> EntityIds, Guid? ContainerEntityId = null);
