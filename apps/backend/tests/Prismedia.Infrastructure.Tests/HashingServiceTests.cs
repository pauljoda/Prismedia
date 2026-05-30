using System.Security.Cryptography;
using Prismedia.Infrastructure.Media.Processing;

namespace Prismedia.Infrastructure.Tests;

public sealed class HashingServiceTests {
    private static string CreateTempFile(int sizeBytes) {
        var path = Path.GetTempFileName();
        var bytes = new byte[sizeBytes];
        for (var i = 0; i < bytes.Length; i++) {
            bytes[i] = (byte)(i % 251);
        }
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public async Task ComputeHashes_WithoutMd5_SkipsMd5AndStillReturnsOshash() {
        var path = CreateTempFile(200 * 1024);
        try {
            var result = await new HashingService().ComputeHashesAsync(path, computeMd5: false, CancellationToken.None);

            Assert.Null(result.Md5);
            Assert.Equal(16, result.Oshash.Length);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ComputeHashes_OshashIsIdenticalWhetherOrNotMd5IsComputed() {
        var path = CreateTempFile(200 * 1024);
        try {
            var service = new HashingService();
            var withMd5 = await service.ComputeHashesAsync(path, computeMd5: true, CancellationToken.None);
            var withoutMd5 = await service.ComputeHashesAsync(path, computeMd5: false, CancellationToken.None);

            // The cheap head+tail path must produce the same oshash as the full streaming pass.
            Assert.Equal(withMd5.Oshash, withoutMd5.Oshash);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ComputeHashes_WithMd5_MatchesReferenceMd5() {
        var path = CreateTempFile(200 * 1024);
        try {
            var expected = Convert.ToHexStringLower(MD5.HashData(await File.ReadAllBytesAsync(path)));

            var result = await new HashingService().ComputeHashesAsync(path, computeMd5: true, CancellationToken.None);

            Assert.Equal(expected, result.Md5);
        } finally {
            File.Delete(path);
        }
    }
}
