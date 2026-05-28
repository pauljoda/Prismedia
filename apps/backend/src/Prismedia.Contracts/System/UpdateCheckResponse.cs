namespace Prismedia.Contracts.System;

/// <summary>
/// Describes the local Prismedia build version and the newest release visible to the update checker.
/// </summary>
/// <param name="Status">Machine-readable status: available, current, or unknown.</param>
/// <param name="LocalVersion">Version reported by the running Prismedia host.</param>
/// <param name="LatestVersion">Newest release version, when it can be resolved.</param>
/// <param name="LatestUrl">Human-facing release URL, when a release is known.</param>
/// <param name="UpdateAvailable">True when the newest release is greater than the local version.</param>
/// <param name="CheckedAt">UTC timestamp for the release check that produced this response.</param>
/// <param name="FromCache">True when the response came from the in-memory release check cache.</param>
/// <param name="Error">Non-fatal reason update state is unknown, when applicable.</param>
public sealed record UpdateCheckResponse(
    string Status,
    string LocalVersion,
    string? LatestVersion,
    string? LatestUrl,
    bool UpdateAvailable,
    DateTimeOffset CheckedAt,
    bool FromCache,
    string? Error);
