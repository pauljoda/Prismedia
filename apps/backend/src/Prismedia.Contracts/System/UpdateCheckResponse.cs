namespace Prismedia.Contracts.System;

/// <summary>
/// Describes the local Prismedia build channel/version and the newest image visible to the
/// update checker for that channel.
/// </summary>
/// <param name="Status">Machine-readable status: available, current, unknown, or development.</param>
/// <param name="Channel">Release channel of the running build: dev, alpha, beta, or release.</param>
/// <param name="LocalVersion">Version reported by the running Prismedia host.</param>
/// <param name="LatestVersion">Newest channel version, when it can be resolved. Null for dev builds, which are compared by image digest rather than version.</param>
/// <param name="LatestUrl">Human-facing page for the newest image, when one is known.</param>
/// <param name="UpdateAvailable">True when a newer image is published on the running build's channel.</param>
/// <param name="CheckedAt">UTC timestamp for the update check that produced this response.</param>
/// <param name="FromCache">True when the response came from the in-memory update check cache.</param>
/// <param name="Error">Non-fatal reason update state is unknown, when applicable.</param>
public sealed record UpdateCheckResponse(
    string Status,
    string Channel,
    string LocalVersion,
    string? LatestVersion,
    string? LatestUrl,
    bool UpdateAvailable,
    DateTimeOffset CheckedAt,
    bool FromCache,
    string? Error);
