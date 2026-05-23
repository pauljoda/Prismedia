using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class EntitySubtitleRow {
    public Guid Id { get; set; }

    public Guid EntityId { get; set; }

    public string Language { get; set; } = string.Empty;

    public string? Label { get; set; }

    public string Format { get; set; } = string.Empty;

    public EntitySubtitleSource Source { get; set; } = EntitySubtitleSource.Manual;

    public string StoragePath { get; set; } = string.Empty;

    public string SourceFormat { get; set; } = string.Empty;

    public string? SourcePath { get; set; }

    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
