namespace Prismedia.Infrastructure.Persistence.Entities;

/// <summary>Pipeline-managed subtitle extraction and sidecar reconciliation state for an entity.</summary>
public sealed class EntitySubtitleStateRow {
    public Guid EntityId { get; set; }

    public DateTimeOffset? SubtitlesExtractedAt { get; set; }

    public string? SubtitleSidecarSignature { get; set; }
}
