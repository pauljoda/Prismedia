namespace Prismedia.Domain.Entities;

public enum AcquisitionImportContentKind {
    [Code("book")] Book,
    [Code("audio")] Audio,
    [Code("video")] Video,
    [Code("image")] Image,
    [Code("subtitle")] Subtitle,
    [Code("archive")] Archive,
    [Code("other")] Other,
}
