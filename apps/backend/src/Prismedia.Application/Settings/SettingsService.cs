using System.Text.Json;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Jobs;
using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Settings;

/// <summary>
/// Application use-case service for app-global settings and watched library roots.
/// Owns registry validation, default derivation, typed settings snapshots, and local
/// directory browsing while delegating raw persistence to <see cref="ISettingsPersistence"/>.
/// </summary>
public sealed partial class SettingsService {
    private readonly ISettingsPersistence _persistence;
    private readonly IJobQueueService? _jobs;
    private readonly ILogger<SettingsService>? _logger;

    /// <summary>
    /// Creates the service over the settings persistence port.
    /// </summary>
    /// <param name="persistence">Persistence adapter implemented by Infrastructure.</param>
    /// <param name="jobs">
    /// Optional job queue used to kick off an immediate scan when a library root is added.
    /// When omitted (for example in infrastructure helpers that only read settings) creation
    /// simply skips the kickoff scan.
    /// </param>
    /// <param name="logger">Optional logger for invalid persisted setting values.</param>
    public SettingsService(
        ISettingsPersistence persistence,
        IJobQueueService? jobs = null,
        ILogger<SettingsService>? logger = null) {
        _persistence = persistence;
        _jobs = jobs;
        _logger = logger;
    }

    /// <summary>
    /// Returns the full app-global settings catalog with effective values.
    /// </summary>
    public async Task<SettingsCatalogResponse> GetCatalogAsync(CancellationToken cancellationToken) {
        var overrides = await _persistence.LoadSettingOverridesAsync(cancellationToken);
        var descriptors = AppSettingsRegistry.Definitions
            .Select(definition => {
                var (value, isDefault) = ResolveEffectiveValue(definition, overrides);
                return definition.ToDescriptor(value, isDefault);
            })
            .ToArray();

        var groups = descriptors
            .GroupBy(descriptor => descriptor.GroupKey)
            .Select(group => {
                var definition = AppSettingsRegistry.Definitions.First(d => d.GroupKey == group.Key);
                return new SettingsGroup(
                    definition.GroupKey,
                    definition.GroupLabel,
                    definition.GroupDescription,
                    definition.GroupOrder,
                    group.OrderBy(setting => setting.Order).ToArray());
            })
            .OrderBy(group => group.Order)
            .ToArray();

        return new SettingsCatalogResponse(groups);
    }

    /// <summary>
    /// Returns one setting descriptor by stable setting key.
    /// </summary>
    public async Task<SettingDescriptor> GetSettingAsync(string key, CancellationToken cancellationToken) {
        var definition = RequireDefinition(key);
        var overrides = await _persistence.LoadSettingOverridesAsync(cancellationToken);
        var (value, isDefault) = ResolveEffectiveValue(definition, overrides);
        return definition.ToDescriptor(value, isDefault);
    }

    /// <summary>
    /// Returns effective setting values keyed by setting key. An empty key list returns all values.
    /// </summary>
    public async Task<SettingsValuesResponse> GetValuesAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken) {
        var requested = keys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal).ToArray();
        var definitions = requested.Length == 0
            ? AppSettingsRegistry.Definitions
            : requested.Select(RequireDefinition).ToArray();
        var overrides = await _persistence.LoadSettingOverridesAsync(cancellationToken);
        var values = definitions.ToDictionary(
            definition => definition.Key,
            definition => ResolveEffectiveValue(definition, overrides).Value,
            StringComparer.Ordinal);
        return new SettingsValuesResponse(values);
    }

    /// <summary>
    /// Validates and saves one setting override, or removes the override when the saved value
    /// equals the registry default.
    /// </summary>
    public async Task<SettingDescriptor> UpdateSettingAsync(
        string key,
        JsonElement value,
        CancellationToken cancellationToken) {
        var definition = RequireDefinition(key);
        var normalized = ValidateOrThrow(definition, value);
        if (SameJson(normalized, definition.DefaultValue)) {
            await _persistence.DeleteSettingOverrideAsync(definition.Key, cancellationToken);
        } else {
            await _persistence.SaveSettingOverrideAsync(definition.Key, normalized.GetRawText(), cancellationToken);
        }

        return definition.ToDescriptor(normalized, SameJson(normalized, definition.DefaultValue));
    }

    /// <summary>
    /// Validates and saves a batch of setting values. All values are validated before any
    /// persistence operation is attempted.
    /// </summary>
    public async Task<SettingsCatalogResponse> UpdateSettingsAsync(
        IReadOnlyDictionary<string, JsonElement> values,
        CancellationToken cancellationToken) {
        var normalized = new Dictionary<SettingDefinition, JsonElement>();
        foreach (var (key, value) in values) {
            var definition = RequireDefinition(key);
            normalized[definition] = ValidateOrThrow(definition, value);
        }

        var overrides = normalized
            .Where(pair => !SameJson(pair.Value, pair.Key.DefaultValue))
            .ToDictionary(pair => pair.Key.Key, pair => pair.Value.GetRawText(), StringComparer.Ordinal);
        var defaults = normalized
            .Where(pair => SameJson(pair.Value, pair.Key.DefaultValue))
            .Select(pair => pair.Key.Key)
            .ToArray();
        await _persistence.ReplaceSettingOverridesAsync(overrides, defaults, cancellationToken);

        return await GetCatalogAsync(cancellationToken);
    }

    /// <summary>
    /// Removes one setting override and returns the defaulted descriptor.
    /// </summary>
    public async Task<SettingDescriptor> ResetSettingAsync(string key, CancellationToken cancellationToken) {
        var definition = RequireDefinition(key);
        await _persistence.DeleteSettingOverrideAsync(definition.Key, cancellationToken);
        return definition.ToDescriptor(definition.DefaultValue, isDefault: true);
    }

    /// <summary>
    /// Returns app-global visibility defaults.
    /// </summary>
    public async Task<VisibilitySettings> GetVisibilitySettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([AppSettings.Visibility.DefaultMode], cancellationToken);
        return new VisibilitySettings(Read(values, AppSettings.Visibility.DefaultMode));
    }

    /// <summary>
    /// Returns scan scheduling settings.
    /// </summary>
    public async Task<ScanSettings> GetScanSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync(
            [AppSettings.Scan.AutoScanEnabled, AppSettings.Scan.IntervalMinutes, AppSettings.Scan.IntegrityIntervalHours],
            cancellationToken);
        return new ScanSettings(
            Read(values, AppSettings.Scan.AutoScanEnabled),
            Read(values, AppSettings.Scan.IntervalMinutes),
            Read(values, AppSettings.Scan.IntegrityIntervalHours));
    }

    /// <summary>Returns the cadence settings for re-searching monitored items.</summary>
    public async Task<MonitoredSearchSettings> GetMonitoredSearchSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync(
            [AppSettings.Monitoring.SearchEnabled, AppSettings.Monitoring.IntervalMinutes],
            cancellationToken);
        return new MonitoredSearchSettings(
            Read(values, AppSettings.Monitoring.SearchEnabled),
            Read(values, AppSettings.Monitoring.IntervalMinutes));
    }

    /// <summary>Returns the acquisition recycle-bin settings (a blank path disables the bin).</summary>
    public async Task<RecycleBinSettings> GetRecycleBinSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync(
            [AppSettings.Acquisition.RecycleBinPath, AppSettings.Acquisition.RecycleBinCleanupDays],
            cancellationToken);
        var path = Read(values, AppSettings.Acquisition.RecycleBinPath);
        return new RecycleBinSettings(
            string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Read(values, AppSettings.Acquisition.RecycleBinCleanupDays));
    }

    /// <summary>
    /// Returns the proper/repack download policy. Decodes the stored
    /// <see cref="ProperDownloadPolicy"/> code, falling back to
    /// <see cref="ProperDownloadPolicy.PreferAndUpgrade"/> when the value is missing or unknown.
    /// </summary>
    public async Task<ProperDownloadSettings> GetProperDownloadSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([AppSettings.Acquisition.DownloadPropers], cancellationToken);
        var code = Read(values, AppSettings.Acquisition.DownloadPropers);
        var policy = code.TryDecodeAs<ProperDownloadPolicy>(out var decoded) ? decoded : ProperDownloadPolicy.PreferAndUpgrade;
        return new ProperDownloadSettings(policy);
    }

    /// <summary>
    /// Returns the preferred acquisition transfer protocol, defaulting safely to Usenet when the
    /// persisted value is missing or unknown. Capability resolution remains the search runner's job.
    /// </summary>
    public async Task<PreferredDownloadProtocolSettings> GetPreferredDownloadProtocolSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([AppSettings.Acquisition.PreferredProtocol], cancellationToken);
        var code = Read(values, AppSettings.Acquisition.PreferredProtocol);
        var protocol = code.TryDecodeAs<DownloadProtocol>(out var decoded) ? decoded : DownloadProtocol.Usenet;
        return new PreferredDownloadProtocolSettings(protocol);
    }

    /// <summary>
    /// Returns recurring collection refresh settings.
    /// </summary>
    public async Task<CollectionRefreshSettings> GetCollectionRefreshSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([AppSettings.Collections.AutoRefreshEnabled], cancellationToken);
        return new CollectionRefreshSettings(Read(values, AppSettings.Collections.AutoRefreshEnabled));
    }

    /// <summary>Returns whether installed plugins should be updated automatically.</summary>
    public async Task<PluginUpdateSettings> GetPluginUpdateSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([AppSettings.Plugins.AutoUpdateEnabled], cancellationToken);
        return new PluginUpdateSettings(Read(values, AppSettings.Plugins.AutoUpdateEnabled));
    }

    /// <summary>
    /// Returns configured metadata-provider defaults keyed by canonical EntityKind code.
    /// Stored provider ids are intentionally not resolved here; provider discovery decides whether
    /// each id is currently installed, enabled, authenticated, and compatible.
    /// </summary>
    public async Task<IdentifyProviderSettings> GetIdentifyProviderSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([AppSettings.Identify.DefaultProviders], cancellationToken);
        return new IdentifyProviderSettings(Read(values, AppSettings.Identify.DefaultProviders));
    }

    /// <summary>
    /// Returns auto-identify settings used to drive plugin identification during scans.
    /// The stored confidence threshold is a 0–100 percentage and is returned here as a 0–1 fraction.
    /// </summary>
    public async Task<AutoIdentifySettings> GetAutoIdentifySettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync(
            [
                AppSettings.AutoIdentify.Enabled,
                AppSettings.AutoIdentify.Providers,
                AppSettings.AutoIdentify.EntityKinds,
                AppSettings.AutoIdentify.ConfidenceThreshold,
                AppSettings.AutoIdentify.UnorganizedOnly
            ],
            cancellationToken);
        var percent = (double)Read(values, AppSettings.AutoIdentify.ConfidenceThreshold);
        return new AutoIdentifySettings(
            Read(values, AppSettings.AutoIdentify.Enabled),
            Read(values, AppSettings.AutoIdentify.Providers),
            Read(values, AppSettings.AutoIdentify.EntityKinds),
            Math.Clamp(percent / 100d, 0d, 1d),
            Read(values, AppSettings.AutoIdentify.UnorganizedOnly));
    }

    /// <summary>
    /// Returns whether library scans should delete tags that nothing references.
    /// </summary>
    public async Task<bool> GetRemoveOrphanTagsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([AppSettings.Taxonomy.RemoveOrphanTags], cancellationToken);
        return Read(values, AppSettings.Taxonomy.RemoveOrphanTags);
    }

    /// <summary>
    /// Returns generation-pipeline settings used by scan and maintenance jobs.
    /// </summary>
    public async Task<GenerationSettings> GetGenerationSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync(
            [
                AppSettings.Generation.AutoGenerateMetadata,
                AppSettings.Generation.AutoGenerateOshash,
                AppSettings.Generation.AutoGenerateMd5,
                AppSettings.Generation.AutoGeneratePreview,
                AppSettings.Generation.GenerateTrickplay,
                AppSettings.Generation.TrickplayIntervalSeconds,
                AppSettings.Generation.PreviewClipDurationSeconds,
                AppSettings.Generation.ThumbnailQuality,
                AppSettings.Generation.TrickplayQuality,
                AppSettings.Generation.MetadataStorageDedicated
            ],
            cancellationToken);

        return new GenerationSettings(
            Read(values, AppSettings.Generation.AutoGenerateMetadata),
            Read(values, AppSettings.Generation.AutoGenerateOshash),
            Read(values, AppSettings.Generation.AutoGenerateMd5),
            Read(values, AppSettings.Generation.AutoGeneratePreview),
            Read(values, AppSettings.Generation.GenerateTrickplay),
            Read(values, AppSettings.Generation.TrickplayIntervalSeconds),
            Read(values, AppSettings.Generation.PreviewClipDurationSeconds),
            SelectInt(Read(values, AppSettings.Generation.ThumbnailQuality), 2),
            SelectInt(Read(values, AppSettings.Generation.TrickplayQuality), 2),
            Read(values, AppSettings.Generation.MetadataStorageDedicated));
    }

    /// <summary>
    /// Returns worker throughput settings.
    /// </summary>
    public async Task<WorkerSettings> GetWorkerSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([AppSettings.Jobs.BackgroundConcurrency], cancellationToken);
        return new WorkerSettings(Read(values, AppSettings.Jobs.BackgroundConcurrency));
    }

    /// <summary>
    /// Returns playback defaults.
    /// </summary>
    public async Task<PlaybackSettings> GetPlaybackSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync(
            [
                AppSettings.Playback.DefaultMode,
                AppSettings.Playback.ShowCastControls,
                AppSettings.Playback.AudioPreferredLanguages
            ],
            cancellationToken);
        return new PlaybackSettings(
            Read(values, AppSettings.Playback.DefaultMode),
            Read(values, AppSettings.Playback.ShowCastControls),
            Read(values, AppSettings.Playback.AudioPreferredLanguages));
    }

    /// <summary>
    /// Returns subtitle behavior and appearance defaults.
    /// </summary>
    public async Task<SubtitleSettings> GetSubtitleSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync(
            [
                AppSettings.Subtitles.AutoEnable,
                AppSettings.Subtitles.PreferredLanguages,
                AppSettings.Subtitles.AutoDownloadEnabled,
                AppSettings.Subtitles.AutoDownloadLanguages,
                AppSettings.Subtitles.AutoDownloadMinimumConfidence,
                AppSettings.Subtitles.Style,
                AppSettings.Subtitles.FontScale,
                AppSettings.Subtitles.PositionPercent,
                AppSettings.Subtitles.Opacity
            ],
            cancellationToken);

        return new SubtitleSettings(
            Read(values, AppSettings.Subtitles.AutoEnable),
            Read(values, AppSettings.Subtitles.PreferredLanguages),
            Read(values, AppSettings.Subtitles.AutoDownloadEnabled),
            Read(values, AppSettings.Subtitles.AutoDownloadLanguages),
            Read(values, AppSettings.Subtitles.AutoDownloadMinimumConfidence),
            Read(values, AppSettings.Subtitles.Style),
            (float)Read(values, AppSettings.Subtitles.FontScale),
            (float)Read(values, AppSettings.Subtitles.PositionPercent),
            (float)Read(values, AppSettings.Subtitles.Opacity));
    }

    /// <summary>
    /// Returns HLS transcoder and ffmpeg settings.
    /// </summary>
    public async Task<HlsSettings> GetHlsSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync(
            [
                AppSettings.Hls.TranscoderProfile,
                AppSettings.Hls.FfmpegPath,
                AppSettings.Hls.VaapiDevice,
                AppSettings.Hls.EnableAdaptiveBitrate,
                AppSettings.Hls.EncodingThreadCount,
                AppSettings.Hls.MaxCacheSizeGb
            ],
            cancellationToken);

        return new HlsSettings(
            Read(values, AppSettings.Hls.TranscoderProfile),
            Read(values, AppSettings.Hls.FfmpegPath),
            Read(values, AppSettings.Hls.VaapiDevice),
            Read(values, AppSettings.Hls.EnableAdaptiveBitrate),
            Read(values, AppSettings.Hls.EncodingThreadCount),
            Read(values, AppSettings.Hls.MaxCacheSizeGb));
    }

    private async Task<IReadOnlyDictionary<SettingDefinition, JsonElement>> GetValueMapAsync(
        IEnumerable<SettingDefinition> definitions,
        CancellationToken cancellationToken) {
        var requested = definitions.ToArray();
        var values = (await GetValuesAsync(requested.Select(definition => definition.Key), cancellationToken)).Values;
        return requested.ToDictionary(definition => definition, definition => values[definition.Key]);
    }

    private static T Read<T>(
        IReadOnlyDictionary<SettingDefinition, JsonElement> values,
        SettingDefinition<T> definition) =>
        definition.Read(values[definition]);

    private static int SelectInt(string value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;

    private (JsonElement Value, bool IsDefault) ResolveEffectiveValue(
        SettingDefinition definition,
        IReadOnlyDictionary<string, string> overrides) {
        if (!overrides.TryGetValue(definition.Key, out var rawJson) || string.IsNullOrWhiteSpace(rawJson)) {
            return (definition.DefaultValue.Clone(), true);
        }

        try {
            using var document = JsonDocument.Parse(rawJson);
            var validated = definition.Validate(document.RootElement);
            if (validated.IsValid) {
                return (validated.Value.Clone(), false);
            }

            _logger?.LogWarning(
                "Stored setting override {SettingKey} is invalid and will be ignored: {Reason}",
                definition.Key,
                validated.Error);
        } catch (JsonException ex) {
            _logger?.LogWarning(ex, "Stored setting override {SettingKey} is invalid JSON and will be ignored.", definition.Key);
        }

        return (definition.DefaultValue.Clone(), true);
    }

    private static SettingDefinition RequireDefinition(string key) =>
        AppSettingsRegistry.Find(key) ?? throw new SettingNotFoundException(key);

    private static JsonElement ValidateOrThrow(SettingDefinition definition, JsonElement value) {
        var validated = definition.Validate(value);
        if (!validated.IsValid) {
            throw new SettingValidationException(definition.Key, validated.Error ?? $"{definition.Key} is invalid.");
        }

        return validated.Value.Clone();
    }

    private static bool SameJson(JsonElement left, JsonElement right) {
        if (left.ValueKind != right.ValueKind) {
            return left.ValueKind == JsonValueKind.Number &&
                right.ValueKind == JsonValueKind.Number &&
                left.TryGetDecimal(out var leftNumber) &&
                right.TryGetDecimal(out var rightNumber) &&
                leftNumber == rightNumber;
        }

        return left.ValueKind switch {
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Number => left.TryGetDecimal(out var leftNumber) &&
                right.TryGetDecimal(out var rightNumber) &&
                leftNumber == rightNumber,
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Array => left.EnumerateArray().SequenceEqual(right.EnumerateArray(), JsonElementEqualityComparer.Instance),
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            _ => string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal)
        };
    }

    private sealed class JsonElementEqualityComparer : IEqualityComparer<JsonElement> {
        public static JsonElementEqualityComparer Instance { get; } = new();

        public bool Equals(JsonElement x, JsonElement y) => SameJson(x, y);

        public int GetHashCode(JsonElement obj) => obj.GetRawText().GetHashCode(StringComparison.Ordinal);
    }

}
