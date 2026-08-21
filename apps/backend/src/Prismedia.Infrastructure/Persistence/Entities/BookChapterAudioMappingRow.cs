namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class BookChapterAudioMappingRow {
    public Guid Id { get; set; }

    public Guid BookId { get; set; }

    public string ReadableChapterKey { get; set; } = string.Empty;

    public Guid AudioTrackEntityId { get; set; }

    /// <summary>Whether a user chose this pair or the server's title matcher derived it.</summary>
    public Domain.Entities.BookChapterMappingOrigin Origin { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
