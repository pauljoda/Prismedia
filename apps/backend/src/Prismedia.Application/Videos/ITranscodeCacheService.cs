namespace Prismedia.Application.Videos;

/// <summary>
/// Application port for measuring, clearing, and size-bounding the on-disk transcode/remux cache so
/// generated HLS output cannot silently fill the host disk.
/// </summary>
public interface ITranscodeCacheService {
    /// <summary>One gigabyte in bytes.</summary>
    private const long BytesPerGigabyte = 1024L * 1024L * 1024L;

    /// <summary>Converts a gigabyte cache limit to bytes; 0 or negative (unlimited) maps to 0.</summary>
    /// <param name="gigabytes">The configured limit in gigabytes.</param>
    /// <returns>The limit in bytes, or 0 when unlimited.</returns>
    static long GigabytesToBytes(int gigabytes) => gigabytes <= 0 ? 0 : gigabytes * BytesPerGigabyte;

    /// <summary>Sums the size on disk of every cached transcode/remux file, in bytes.</summary>
    long ComputeSizeBytes();

    /// <summary>
    /// Removes the entire transcode/remux cache, cancelling in-flight jobs first. Source media is
    /// never touched.
    /// </summary>
    /// <returns>The number of bytes freed.</returns>
    long Clear();

    /// <summary>
    /// Evicts the least-recently-played cached items until the cache is within
    /// <paramref name="maxBytes"/>. Items currently playing are never evicted.
    /// </summary>
    /// <param name="maxBytes">The size ceiling in bytes; 0 or negative means unlimited (no eviction).</param>
    /// <param name="protectedItemIds">
    /// Items with a live viewer, kept regardless of age. This must be the union of recently-pinged
    /// playback sessions and items whose assets were recently requested: a stalled client stops sending
    /// heartbeats while it retries a segment, and evicting its segments then turns a recoverable stall
    /// into an unrecoverable 404 loop.
    /// </param>
    /// <returns>The number of cached items evicted.</returns>
    int PruneToLimit(long maxBytes, IReadOnlySet<Guid> protectedItemIds);
}
