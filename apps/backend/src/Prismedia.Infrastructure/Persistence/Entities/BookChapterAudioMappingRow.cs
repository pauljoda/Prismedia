namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class BookChapterAudioMappingRow {
    public Guid Id { get; set; }

    public Guid BookId { get; set; }

    public string ReadableChapterKey { get; set; } = string.Empty;

    public Guid AudioTrackEntityId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
