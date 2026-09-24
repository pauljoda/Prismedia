using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Integrations;

/// <summary>Immutable byte evidence for one source output. RelativePath is a suggested portable name, never a destination path.</summary>
public sealed record IntegrationArtifact(string Id, string ItemId, string RelativePath, string MediaType,
    long SizeBytes, string Sha256, IntegrationArtifactRole Role, string? GroupId = null, int? Ordinal = null);
