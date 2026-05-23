namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class VideoDetailRow {
    public Guid EntityId { get; set; }

    public Guid? LibraryRootId { get; set; }

    public DateTimeOffset? SubtitlesExtractedAt { get; set; }
}
