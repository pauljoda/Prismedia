using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prismedia.Application.Settings;
using Prismedia.Infrastructure.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed class RecycleBinTests {
    [Fact]
    public async Task RecyclingAnOldMediaFileStartsANewRetentionWindow() {
        var root = Directory.CreateTempSubdirectory("prismedia-recycle-retention-").FullName;
        try {
            await using var db = new PrismediaDbContext(new DbContextOptionsBuilder<PrismediaDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var settings = new SettingsService(new EfSettingsPersistence(db));
            await settings.UpdateSettingAsync(AppSettings.Acquisition.RecycleBinPath.Key,
                JsonSerializer.SerializeToElement(Path.Combine(root, "bin")), default);
            var bin = new RecycleBin(settings, NullLogger<RecycleBin>.Instance);
            var original = Path.Combine(root, "old-media.prismedia-bak");
            await File.WriteAllTextAsync(original, "original-library-bytes");
            File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddYears(-10));
            var began = DateTime.UtcNow.AddSeconds(-1);

            var recycled = Assert.IsType<string>(await bin.TryMoveToBinAsync(original, default));
            Assert.Equal(0, await bin.CleanupAsync(default));
            Assert.Equal("original-library-bytes", await File.ReadAllTextAsync(recycled));
            Assert.True(File.GetLastWriteTimeUtc(recycled) >= began);

            File.SetLastWriteTimeUtc(recycled, DateTime.UtcNow.AddDays(-8));
            Assert.Equal(1, await bin.CleanupAsync(default));
            Assert.False(File.Exists(recycled));
        } finally { Directory.Delete(root, true); }
    }
}
