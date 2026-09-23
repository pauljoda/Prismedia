namespace Prismedia.Contracts.Entities;

/// <summary>The physical audiobook position paired with one canonical Book progress report.</summary>
/// <param name="TrackEntityId">Playable audio track belonging to the Book.</param>
/// <param name="MarkerId">Optional embedded chapter marker within the track.</param>
/// <param name="OffsetSeconds">Exact position in the physical track, from its beginning.</param>
public sealed record BookListeningPositionRequest(
    Guid TrackEntityId,
    Guid? MarkerId,
    double OffsetSeconds);
