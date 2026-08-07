using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;
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
            definition.Key == AppSettings.Visibility.DefaultMode.Key &&
            definition.Type == SettingValueType.Select &&
            definition.DefaultValue.GetString() == "off");
        Assert.Contains(definitions, definition =>
            definition.Key == AppSettings.Jobs.BackgroundConcurrency.Key &&
            definition.Type == SettingValueType.Integer &&
            definition.Constraints?.Min == 1 &&
            definition.Constraints?.Max == 32);
        Assert.Contains(definitions, definition =>
            definition.Key == AppSettings.Collections.AutoRefreshEnabled.Key &&
            definition.Type == SettingValueType.Boolean &&
            definition.DefaultValue.GetBoolean());
        Assert.Contains(definitions, definition =>
            definition.Key == AppSettings.Plugins.AutoUpdateEnabled.Key &&
            definition.Type == SettingValueType.Boolean &&
            definition.DefaultValue.GetBoolean());
        Assert.Contains(definitions, definition =>
            definition.Key == AppSettings.Identify.DefaultProviders.Key &&
            definition.Type == SettingValueType.StringMap &&
            definition.DefaultValue.ValueKind == JsonValueKind.Object &&
            !definition.DefaultValue.EnumerateObject().Any());
        Assert.Contains(definitions, definition =>
            definition.Key == AppSettings.Subtitles.PreferredLanguages.Key &&
            definition.Type == SettingValueType.WeightedTermList &&
            definition.Constraints?.Min == -100 &&
            definition.Constraints?.Max == 100);

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
            new[] { AppSettings.Visibility.DefaultMode.Key, AppSettings.Jobs.BackgroundConcurrency.Key },
            CancellationToken.None);

        Assert.Equal("off", defaults.Values[AppSettings.Visibility.DefaultMode.Key].GetString());
        Assert.Equal(4, defaults.Values[AppSettings.Jobs.BackgroundConcurrency.Key].GetInt32());
        Assert.Empty(await db.AppSettings.ToArrayAsync());

        var updated = await service.UpdateSettingAsync(
            AppSettings.Jobs.BackgroundConcurrency.Key,
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
            AppSettings.Playback.DefaultMode.Key,
            JsonSerializer.SerializeToElement("hls"),
            CancellationToken.None);

        var reset = await service.ResetSettingAsync(AppSettings.Playback.DefaultMode.Key, CancellationToken.None);

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
                AppSettings.Jobs.BackgroundConcurrency.Key,
                JsonSerializer.SerializeToElement(99),
                CancellationToken.None));

        Assert.Equal(AppSettings.Jobs.BackgroundConcurrency.Key, ex.Key);
        Assert.Contains("between 1 and 32", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubtitlePreferenceTermsNormalizeLegacyListsAndPreserveExplicitWeights() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        var legacy = await service.UpdateSettingAsync(
            AppSettings.Subtitles.PreferredLanguages.Key,
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
            AppSettings.Subtitles.PreferredLanguages.Key,
            JsonSerializer.SerializeToElement(new[] {
                new SubtitlePreferenceTerm("Forced", -80),
                new SubtitlePreferenceTerm("English", 55),
                new SubtitlePreferenceTerm("Eng", 35),
            }),
            CancellationToken.None);
        var snapshot = await service.GetSubtitleSettingsAsync(CancellationToken.None);
        var weightedTerms = weighted.Value.Deserialize<SubtitlePreferenceTerm[]>();

        Assert.NotNull(weightedTerms);
        Assert.Equal(
            [
                new SubtitlePreferenceTerm("Forced", -80),
                new SubtitlePreferenceTerm("English", 55),
                new SubtitlePreferenceTerm("Eng", 35),
            ],
            weightedTerms);
        Assert.Equal(weightedTerms, snapshot.PreferredTerms);
    }

    [Theory]
    [InlineData("""[{"term":"","weight":50}]""")]
    [InlineData("""[{"term":"English","weight":-101}]""")]
    [InlineData("""[{"term":"English","weight":101}]""")]
    [InlineData("""[{"term":"English","weight":50},{"term":"english","weight":40}]""")]
    public async Task SubtitlePreferenceTermsRejectInvalidRules(string json) {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));
        using var document = JsonDocument.Parse(json);

        var exception = await Assert.ThrowsAsync<SettingValidationException>(() =>
            service.UpdateSettingAsync(
                AppSettings.Subtitles.PreferredLanguages.Key,
                document.RootElement,
                CancellationToken.None));

        Assert.Equal(AppSettings.Subtitles.PreferredLanguages.Key, exception.Key);
    }

    [Fact]
    public async Task SnapshotsExposeTypedValuesForBackendConsumers() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));

        await service.UpdateSettingsAsync(
            new Dictionary<string, JsonElement> {
                [AppSettings.Scan.AutoScanEnabled.Key] = JsonSerializer.SerializeToElement(true),
                [AppSettings.Scan.IntervalMinutes.Key] = JsonSerializer.SerializeToElement(15),
                [AppSettings.Collections.AutoRefreshEnabled.Key] = JsonSerializer.SerializeToElement(false),
                [AppSettings.Plugins.AutoUpdateEnabled.Key] = JsonSerializer.SerializeToElement(false),
                [AppSettings.Playback.AudioPreferredLanguages.Key] =
                    JsonSerializer.SerializeToElement(new[] { "ja", "jpn" }),
            },
            CancellationToken.None);

        var scan = await service.GetScanSettingsAsync(CancellationToken.None);
        var collections = await service.GetCollectionRefreshSettingsAsync(CancellationToken.None);
        var pluginUpdates = await service.GetPluginUpdateSettingsAsync(CancellationToken.None);
        var playback = await service.GetPlaybackSettingsAsync(CancellationToken.None);

        Assert.True(scan.AutoScanEnabled);
        Assert.Equal(15, scan.IntervalMinutes);
        Assert.False(collections.AutoRefreshEnabled);
        Assert.False(pluginUpdates.AutoUpdateEnabled);
        Assert.Equal(["ja", "jpn"], playback.AudioPreferredLanguages);
    }

    [Fact]
    public async Task IdentifyProviderDefaultsSerializeEveryKnownEntityKindAndTrimProviderIds() {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));
        var configured = EntityKindRegistry.All.ToDictionary(
            descriptor => descriptor.Code,
            descriptor => $"  provider-{descriptor.Kind}  ",
            StringComparer.Ordinal);

        var updated = await service.UpdateSettingAsync(
            AppSettings.Identify.DefaultProviders.Key,
            JsonSerializer.SerializeToElement(configured),
            CancellationToken.None);
        var snapshot = await service.GetIdentifyProviderSettingsAsync(CancellationToken.None);

        Assert.Equal(EntityKindRegistry.All.Count, updated.Value.EnumerateObject().Count());
        Assert.Equal(EntityKindRegistry.All.Count, snapshot.DefaultProviders.Count);
        foreach (var descriptor in EntityKindRegistry.All) {
            Assert.Equal($"provider-{descriptor.Kind}", snapshot.DefaultProviders[descriptor.Code]);
        }

        var row = await db.AppSettings.SingleAsync();
        Assert.Equal(AppSettings.Identify.DefaultProviders.Key, row.Key);
        Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(row.ValueJson).RootElement.ValueKind);
    }

    [Theory]
    [InlineData("""{"unknown-kind":"provider"}""")]
    [InlineData("""{"video":42}""")]
    [InlineData("""{"video":"  "}""")]
    public async Task IdentifyProviderDefaultsRejectUnknownKindsAndInvalidProviderIds(string json) {
        await using var db = CreateContext();
        var service = new SettingsService(new EfSettingsPersistence(db));
        using var document = JsonDocument.Parse(json);

        var exception = await Assert.ThrowsAsync<SettingValidationException>(() =>
            service.UpdateSettingAsync(
                AppSettings.Identify.DefaultProviders.Key,
                document.RootElement,
                CancellationToken.None));

        Assert.Equal(AppSettings.Identify.DefaultProviders.Key, exception.Key);
        Assert.Empty(await db.AppSettings.ToArrayAsync());
    }

    private static PrismediaDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"app-settings-{Guid.NewGuid():N}")
            .Options;

        return new PrismediaDbContext(options);
    }
}
