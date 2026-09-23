using Prismedia.Domain.Entities;

namespace Prismedia.Contracts.Entities;

/// <summary>An exact playable track and marker position retained when reading becomes the last activity.</summary>
public sealed record BookListeningProgress(
    Guid TrackEntityId,
    Guid? MarkerId,
    double OffsetSeconds,
    Guid CurrentEntityId,
    ProgressUnit Unit,
    int Index,
    int Total,
    DateTimeOffset UpdatedAt);
