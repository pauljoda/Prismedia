using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Settings;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed class SettingsServiceTests {
    [Fact]
    public async Task CatalogUsesRegistryDefaultsWithoutCreatingRows() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        var catalog = await service.GetCatalogAsync(CancellationToken.None);
        var castControls = catalog.Groups
            .SelectMany(group => group.Settings)
            .Single(setting => setting.Key == AppSettingKeys.PlaybackShowCastControls);

        Assert.True(castControls.Value.GetBoolean());
        Assert.True(castControls.IsDefault);
        Assert.Empty(await db.AppSettings.ToArrayAsync());
    }

    [Fact]
    public async Task UpdatePersistsOnlyNonDefaultOverrides() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        await service.UpdateSettingAsync(
            AppSettingKeys.HlsTranscoderProfile,
            JsonSerializer.SerializeToElement("VideoToolbox"),
            CancellationToken.None);

        var row = await db.AppSettings.SingleAsync();
        Assert.Equal(AppSettingKeys.HlsTranscoderProfile, row.Key);
        Assert.Equal("\"VideoToolbox\"", row.ValueJson);
    }

    [Fact]
    public async Task BatchUpdatePersistsStringListAndPaths() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        await service.UpdateSettingsAsync(
            new Dictionary<string, JsonElement> {
                [AppSettingKeys.PlaybackAudioPreferredLanguages] =
                    JsonSerializer.SerializeToElement(new[] { "ja", "jpn" }),
                [AppSettingKeys.HlsFfmpegPath] =
                    JsonSerializer.SerializeToElement("/opt/homebrew/bin/ffmpeg"),
            },
            CancellationToken.None);

        var playback = await service.GetPlaybackSettingsAsync(CancellationToken.None);
        var hls = await service.GetHlsSettingsAsync(CancellationToken.None);

        Assert.Equal(["ja", "jpn"], playback.AudioPreferredLanguages);
        Assert.Equal("/opt/homebrew/bin/ffmpeg", hls.FfmpegPath);
        Assert.Equal(2, await db.AppSettings.CountAsync());
    }

    [Fact]
    public async Task SavingDecimalDefaultRemovesOverride() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        await service.UpdateSettingAsync(
            AppSettingKeys.SubtitlesOpacity,
            JsonSerializer.SerializeToElement(0.8m),
            CancellationToken.None);
        var reset = await service.UpdateSettingAsync(
            AppSettingKeys.SubtitlesOpacity,
            JsonSerializer.SerializeToElement(1.0m),
            CancellationToken.None);

        Assert.True(reset.IsDefault);
        Assert.Empty(await db.AppSettings.ToArrayAsync());
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"settings-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }
}
