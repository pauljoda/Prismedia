using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>An exact readable Book position retained when listening becomes the last activity.</summary>
public sealed record BookReadingProgress(
    Guid CurrentEntityId,
    ProgressUnit Unit,
    int Index,
    int Total,
    ReaderMode? Mode,
    string? Location,
    DateTimeOffset UpdatedAt);
