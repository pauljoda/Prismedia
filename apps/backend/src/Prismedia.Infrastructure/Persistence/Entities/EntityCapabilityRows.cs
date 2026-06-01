using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class EntityDescriptionRow {
    public Guid EntityId { get; set; }
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityPlaybackRow {
    public Guid EntityId { get; set; }
    public int PlayCount { get; set; }
    public double PlayDurationSeconds { get; set; }
    public double ResumeSeconds { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityStatRow {
    public Guid EntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityDateRow {
    public Guid EntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateOnly? SortableValue { get; set; }
    public string? Precision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityTechnicalRow {
    public Guid EntityId { get; set; }
    public double? DurationSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? FrameRate { get; set; }
    public int? BitRate { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public string? Codec { get; set; }
    public string? Container { get; set; }
    public string? Format { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntitySourceRow {
    public Guid EntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityProgressRow {
    public Guid EntityId { get; set; }
    public Guid? CurrentEntityId { get; set; }
    public string Unit { get; set; } = "item";
    public int Index { get; set; }
    public int Total { get; set; }
    public string? Mode { get; set; }
    public string? Location { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityPositionRow {
    public Guid EntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Value { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityClassificationRow {
    public Guid EntityId { get; set; }
    public string? Value { get; set; }
    public string? System { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityLifetimeRow {
    public Guid EntityId { get; set; }
    public string? StartCode { get; set; }
    public string? StartValue { get; set; }
    public DateOnly? StartSortableValue { get; set; }
    public string? StartPrecision { get; set; }
    public string? EndCode { get; set; }
    public string? EndValue { get; set; }
    public DateOnly? EndSortableValue { get; set; }
    public string? EndPrecision { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EntityFileFingerprintRow {
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public Guid? EntityFileId { get; set; }
    public FingerprintAlgorithm Algorithm { get; set; }
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
