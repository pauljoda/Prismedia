namespace Prismedia.Application.Jobs.Ports;

/// <summary>
/// Port for computing file fingerprints (MD5 and oshash).
/// </summary>
public interface IMediaHashing {
    /// <summary>
    /// Computes the lightweight oshash for a file and, when <paramref name="computeMd5"/> is true,
    /// also the full-file MD5. oshash only reads the file's head and tail, while MD5 streams every
    /// byte — so callers that do not need MD5 should pass <c>false</c> to avoid the full read.
    /// </summary>
    /// <param name="filePath">Absolute path to the file to hash.</param>
    /// <param name="computeMd5">When true, stream the whole file to also produce an MD5 checksum.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The computed hashes; <see cref="FileHashData.Md5"/> is null when MD5 was not requested.</returns>
    Task<FileHashData> ComputeHashesAsync(string filePath, bool computeMd5, CancellationToken cancellationToken);
}

/// <summary>
/// Computed file fingerprints. <see cref="Md5"/> is null when MD5 computation was not requested.
/// </summary>
public sealed record FileHashData(string? Md5, string Oshash);
