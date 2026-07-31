namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class VideoDetailRow {
    public Guid EntityId { get; set; }

    // Kept unmapped solely so older in-memory fixtures compile while their setup moves to the
    // shared attachments. Runtime persistence uses EntityLibraryRootRow and EntitySubtitleStateRow.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public Guid? LibraryRootId { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DateTimeOffset? SubtitlesExtractedAt { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? SubtitleSidecarSignature { get; set; }
}
