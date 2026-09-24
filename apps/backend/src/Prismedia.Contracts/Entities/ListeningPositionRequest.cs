namespace Prismedia.Contracts.Entities;

/// <summary>
/// The exact physical audio position reported with a listening progress update. The server records
/// it as the work's listening checkpoint and places the shared cursor from it.
/// </summary>
/// <param name="TrackEntityId">Playable audio track belonging to the work.</param>
/// <param name="MarkerId">Optional embedded chapter marker within the track; ignored when it no longer exists.</param>
/// <param name="OffsetSeconds">Exact position in the physical track, from its beginning.</param>
public sealed record ListeningPositionRequest(
    Guid TrackEntityId,
    Guid? MarkerId,
    double OffsetSeconds);
