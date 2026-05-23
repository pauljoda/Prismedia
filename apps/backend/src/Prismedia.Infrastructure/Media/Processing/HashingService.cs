using System.Buffers;
using System.Security.Cryptography;

namespace Prismedia.Infrastructure.Media.Processing;

/// <summary>
/// Computes MD5 and oshash fingerprints for media files. The oshash algorithm is compatible
/// with the OpenSubtitles hash used by the Node.js predecessor: file size + sum of 64-bit LE
/// words from the first and last 64 KB.
/// </summary>
public sealed class HashingService {
    private const int OshashChunkBytes = 64 * 1024;
    private const int HashReadBufferBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Computes MD5 and oshash in a single streaming pass over the file, plus a seek-read for the tail.
    /// </summary>
    public async Task<HashResult> ComputeHashesAsync(string filePath, CancellationToken cancellationToken) {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("File not found for hashing.", filePath);

        var fileSize = fileInfo.Length;
        var head = new byte[OshashChunkBytes];
        var headFilled = 0;

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = ArrayPool<byte>.Shared.Rent(HashReadBufferBytes);

        try {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: HashReadBufferBytes, useAsync: true);

            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, HashReadBufferBytes), cancellationToken)) > 0) {
                md5.AppendData(buffer, 0, bytesRead);

                if (headFilled < OshashChunkBytes) {
                    var take = Math.Min(OshashChunkBytes - headFilled, bytesRead);
                    Buffer.BlockCopy(buffer, 0, head, headFilled, take);
                    headFilled += take;
                }
            }
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var tail = new byte[OshashChunkBytes];
        if (fileSize >= OshashChunkBytes) {
            await using var tailStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: OshashChunkBytes, useAsync: true);
            tailStream.Seek(fileSize - OshashChunkBytes, SeekOrigin.Begin);
            int totalRead = 0;
            while (totalRead < OshashChunkBytes) {
                var read = await tailStream.ReadAsync(tail.AsMemory(totalRead, OshashChunkBytes - totalRead), cancellationToken);
                if (read == 0) break;
                totalRead += read;
            }
        } else if (fileSize > 0) {
            Buffer.BlockCopy(head, 0, tail, 0, Math.Min(headFilled, OshashChunkBytes));
        }

        var hash = (ulong)fileSize;
        for (var i = 0; i <= OshashChunkBytes - 8; i += 8) {
            hash += BitConverter.ToUInt64(head, i);
            hash += BitConverter.ToUInt64(tail, i);
        }

        var md5Bytes = md5.GetHashAndReset();
        var md5Hex = Convert.ToHexStringLower(md5Bytes);
        var oshashHex = hash.ToString("x16");

        return new HashResult(md5Hex, oshashHex);
    }
}

public sealed record HashResult(string Md5, string Oshash);
