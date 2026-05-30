namespace Prismedia.Infrastructure.StashCompat;

/// <summary>
/// Lookup inputs handed to a Stash scraper, derived from a Prismedia identify request.
/// Mirrors the fields a Stash scene fragment can carry.
/// </summary>
/// <param name="Url">Direct URL for by-URL lookups.</param>
/// <param name="Title">Title/query text for by-name and query-fragment lookups.</param>
/// <param name="FilePath">Primary source file path, used to derive <c>{filename}</c>.</param>
/// <param name="Checksum">MD5 checksum, when known.</param>
/// <param name="Oshash">OpenSubtitles hash, when known.</param>
/// <param name="Phash">Perceptual hash, when known.</param>
public sealed record StashScrapeInput(
    string? Url = null,
    string? Title = null,
    string? FilePath = null,
    string? Checksum = null,
    string? Oshash = null,
    string? Phash = null);
