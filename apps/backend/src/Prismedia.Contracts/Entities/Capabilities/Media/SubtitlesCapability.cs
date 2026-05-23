using EntitySubtitle = Prismedia.Domain.Capabilities.CapabilitySubtitles.Item;

namespace Prismedia.Contracts.Entities;

/// <summary>API-facing subtitle capability.</summary>
[CapabilityKind("subtitles")]
public sealed record SubtitlesCapability(IReadOnlyList<EntitySubtitle> Items) : EntityCapability;
