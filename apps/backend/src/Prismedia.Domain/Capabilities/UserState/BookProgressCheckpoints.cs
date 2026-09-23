using Prismedia.Domain.Entities;

namespace Prismedia.Domain.Capabilities;

/// <summary>An exact reading cursor retained when listening becomes the Book's last activity.</summary>
public sealed record BookReadingCheckpoint(
    Guid CurrentEntityId,
    ProgressUnit Unit,
    int Index,
    int Total,
    ReaderMode? Mode,
    string? Location,
    DateTimeOffset UpdatedAt);

/// <summary>An exact playable track position retained when reading becomes the Book's last activity.</summary>
public sealed record BookListeningCheckpoint(
    Guid TrackEntityId,
    Guid? MarkerId,
    double OffsetSeconds,
    Guid CurrentEntityId,
    ProgressUnit Unit,
    int Index,
    int Total,
    DateTimeOffset UpdatedAt);
