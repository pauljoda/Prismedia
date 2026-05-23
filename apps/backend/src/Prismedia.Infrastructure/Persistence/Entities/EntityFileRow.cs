using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class EntityFileRow {
    /// <summary>File identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning entity.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Semantic role: source, thumbnail, preview, etc.</summary>
    public EntityFileRole Role { get; set; } = EntityFileRole.Source;

    /// <summary>App-resolved URL path to the file.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Optional MIME type for HTTP serving.</summary>
    public string? MimeType { get; set; }

    /// <summary>Optional file size in bytes.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// How this file was created: <c>"scan"</c> for scan-generated assets,
    /// <c>"custom"</c> for user-uploaded or scraper-provided assets.
    /// Scan jobs never overwrite custom files.
    /// </summary>
    public string Source { get; set; } = "scan";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
