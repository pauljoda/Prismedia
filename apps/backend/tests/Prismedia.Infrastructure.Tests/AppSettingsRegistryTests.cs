using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Settings;

namespace Prismedia.Infrastructure.Tests;

public sealed class AppSettingsRegistryTests {
    [Fact]
    public void RegistryDefinesUniqueKeysWithValidDefaults() {
        var definitions = AppSettingsRegistry.Definitions;

        Assert.NotEmpty(definitions);
        Assert.Equal(definitions.Count, definitions.Select(definition => definition.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(definitions, definition =>
            definition.Key == AppSettingKeys.VisibilityDefaultMode &&
            definition.Type == SettingValueType.Select &&
            definition.DefaultValue.GetString() == "off");
        Assert.Contains(definitions, definition =>
            definition.Key == AppSettingKeys.JobsBackgroundConcurrency &&
            definition.Type == SettingValueType.Integer &&
            definition.Constraints?.Min == 1 &&
            definition.Constraints?.Max == 32);
        Assert.Contains(definitions, definition =>
            definition.Key == AppSettingKeys.CollectionsAutoRefreshEnabled &&
            definition.Type == SettingValueType.Boolean &&
            definition.DefaultValue.GetBoolean());

        foreach (var definition in definitions) {
            var validated = definition.Validate(definition.DefaultValue);
            Assert.True(validated.IsValid, $"{definition.Key}: {validated.Error}");
        }
    }

    [Fact]
    public async Task ValuesUseDefaultsUntilOverridesAreSaved() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        var defaults = await service.GetValuesAsync(
            new[] { AppSettingKeys.VisibilityDefaultMode, AppSettingKeys.JobsBackgroundConcurrency },
            CancellationToken.None);

        Assert.Equal("off", defaults.Values[AppSettingKeys.VisibilityDefaultMode].GetString());
        Assert.Equal(4, defaults.Values[AppSettingKeys.JobsBackgroundConcurrency].GetInt32());
        Assert.Empty(await db.AppSettings.ToArrayAsync());

        var updated = await service.UpdateSettingAsync(
            AppSettingKeys.JobsBackgroundConcurrency,
            JsonSerializer.SerializeToElement(8),
            CancellationToken.None);

        Assert.Equal(8, updated.Value.GetInt32());
        Assert.False(updated.IsDefault);
        Assert.Single(await db.AppSettings.ToArrayAsync());
    }

    [Fact]
    public async Task ResettingSettingRemovesOverrideAndRestoresDefault() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        await service.UpdateSettingAsync(
            AppSettingKeys.PlaybackDefaultMode,
            JsonSerializer.SerializeToElement("hls"),
            CancellationToken.None);

        var reset = await service.ResetSettingAsync(AppSettingKeys.PlaybackDefaultMode, CancellationToken.None);

        Assert.Equal("direct", reset.Value.GetString());
        Assert.True(reset.IsDefault);
        Assert.Empty(await db.AppSettings.ToArrayAsync());
    }

    [Fact]
    public async Task InvalidValuesAreRejectedWithSettingKey() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        var ex = await Assert.ThrowsAsync<SettingValidationException>(() =>
            service.UpdateSettingAsync(
                AppSettingKeys.JobsBackgroundConcurrency,
                JsonSerializer.SerializeToElement(99),
                CancellationToken.None));

        Assert.Equal(AppSettingKeys.JobsBackgroundConcurrency, ex.Key);
        Assert.Contains("between 1 and 32", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubtitlePreferenceTermsNormalizeLegacyListsAndPreserveExplicitWeights() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        var legacy = await service.UpdateSettingAsync(
            AppSettingKeys.SubtitlesPreferredLanguages,
            JsonSerializer.SerializeToElement(new[] { "Forced", "English", "Eng" }),
            CancellationToken.None);
        var legacyTerms = legacy.Value.Deserialize<SubtitlePreferenceTerm[]>();

        Assert.NotNull(legacyTerms);
        Assert.Equal(
            [
                new SubtitlePreferenceTerm("Forced", 100),
                new SubtitlePreferenceTerm("English", 99),
                new SubtitlePreferenceTerm("Eng", 98),
            ],
            legacyTerms);

        var weighted = await service.UpdateSettingAsync(
            AppSettingKeys.SubtitlesPreferredLanguages,
            JsonSerializer.SerializeToElement(new[] {
                new SubtitlePreferenceTerm("Forced", 80),
                new SubtitlePreferenceTerm("English", 55),
                new SubtitlePreferenceTerm("Eng", 35),
            }),
            CancellationToken.None);
        var snapshot = await service.GetSubtitleSettingsAsync(CancellationToken.None);
        var weightedTerms = weighted.Value.Deserialize<SubtitlePreferenceTerm[]>();

        Assert.NotNull(weightedTerms);
        Assert.Equal(
            [
                new SubtitlePreferenceTerm("Forced", 80),
                new SubtitlePreferenceTerm("English", 55),
                new SubtitlePreferenceTerm("Eng", 35),
            ],
            weightedTerms);
        Assert.Equal(weightedTerms, snapshot.PreferredTerms);
    }

    [Theory]
    [InlineData("""[{"term":"","weight":50}]""")]
    [InlineData("""[{"term":"English","weight":0}]""")]
    [InlineData("""[{"term":"English","weight":101}]""")]
    [InlineData("""[{"term":"English","weight":50},{"term":"english","weight":40}]""")]
    public async Task SubtitlePreferenceTermsRejectInvalidRules(string json) {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));
        using var document = JsonDocument.Parse(json);

        var exception = await Assert.ThrowsAsync<SettingValidationException>(() =>
            service.UpdateSettingAsync(
                AppSettingKeys.SubtitlesPreferredLanguages,
                document.RootElement,
                CancellationToken.None));

        Assert.Equal(AppSettingKeys.SubtitlesPreferredLanguages, exception.Key);
    }

    [Fact]
    public async Task SnapshotsExposeTypedValuesForBackendConsumers() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        await service.UpdateSettingsAsync(
            new Dictionary<string, JsonElement> {
                [AppSettingKeys.ScanAutoScanEnabled] = JsonSerializer.SerializeToElement(true),
                [AppSettingKeys.ScanIntervalMinutes] = JsonSerializer.SerializeToElement(15),
                [AppSettingKeys.CollectionsAutoRefreshEnabled] = JsonSerializer.SerializeToElement(false),
                [AppSettingKeys.PlaybackAudioPreferredLanguages] =
                    JsonSerializer.SerializeToElement(new[] { "ja", "jpn" }),
            },
            CancellationToken.None);

        var scan = await service.GetScanSettingsAsync(CancellationToken.None);
        var collections = await service.GetCollectionRefreshSettingsAsync(CancellationToken.None);
        var playback = await service.GetPlaybackSettingsAsync(CancellationToken.None);

        Assert.True(scan.AutoScanEnabled);
        Assert.Equal(15, scan.IntervalMinutes);
        Assert.False(collections.AutoRefreshEnabled);
        Assert.Equal(["ja", "jpn"], playback.AudioPreferredLanguages);
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"app-settings-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }
}
