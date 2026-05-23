namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class MediaSourceRow {
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public Guid? EntityFileId { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Protocol { get; set; } = "File";
    public string? Container { get; set; }
    public string? Name { get; set; }
    public long? SizeBytes { get; set; }
    public double? DurationSeconds { get; set; }
    public int? BitRate { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? FrameRate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MediaStreamRow {
    public Guid Id { get; set; }
    public Guid MediaSourceId { get; set; }
    public Guid EntityId { get; set; }
    public int StreamIndex { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? FrameRate { get; set; }
    public int? BitRate { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public string? PixelFormat { get; set; }
    public int? BitDepth { get; set; }
    public string? ColorRange { get; set; }
    public string? ColorSpace { get; set; }
    public string? ColorTransfer { get; set; }
    public string? ColorPrimaries { get; set; }
    public int? DvProfile { get; set; }
    public int? DvLevel { get; set; }
    public bool? RpuPresentFlag { get; set; }
    public bool? ElPresentFlag { get; set; }
    public bool? BlPresentFlag { get; set; }
    public int? DvBlSignalCompatibilityId { get; set; }
    public bool Hdr10PlusPresentFlag { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TrickplayInfoRow {
    public Guid EntityId { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public int ThumbnailCount { get; set; }
    public double IntervalSeconds { get; set; }
    public int Bandwidth { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
