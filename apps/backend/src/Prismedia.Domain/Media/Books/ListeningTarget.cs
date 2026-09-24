namespace Prismedia.Domain.Media.Books;

/// <summary>A physical audio position a client can start playback at.</summary>
/// <param name="TrackEntityId">Physical playable audio track.</param>
/// <param name="MarkerId">Embedded chapter marker holding the position, when known.</param>
/// <param name="OffsetSeconds">Offset from the track's beginning.</param>
public sealed record ListeningTarget(Guid TrackEntityId, Guid? MarkerId, double OffsetSeconds);
