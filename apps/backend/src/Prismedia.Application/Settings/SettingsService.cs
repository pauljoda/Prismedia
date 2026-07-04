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
        var values = await GetValueMapAsync([
            AppSettingKeys.VisibilityDefaultMode,
            AppSettingKeys.VisibilityLanAutoEnable,
        ], cancellationToken);

        return new VisibilitySettings(
            GetString(values, AppSettingKeys.VisibilityDefaultMode),
            GetBoolean(values, AppSettingKeys.VisibilityLanAutoEnable));
    }

    /// <summary>
    /// Returns scan scheduling settings.
    /// </summary>
    public async Task<ScanSettings> GetScanSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([
            AppSettingKeys.ScanAutoScanEnabled,
            AppSettingKeys.ScanIntervalMinutes,
        ], cancellationToken);

        return new ScanSettings(
            GetBoolean(values, AppSettingKeys.ScanAutoScanEnabled),
            GetInt(values, AppSettingKeys.ScanIntervalMinutes));
    }

    /// <summary>Returns the cadence settings for re-searching monitored items.</summary>
    public async Task<MonitoredSearchSettings> GetMonitoredSearchSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([
            AppSettingKeys.MonitoringSearchEnabled,
            AppSettingKeys.MonitoringIntervalMinutes,
        ], cancellationToken);

        return new MonitoredSearchSettings(
            GetBoolean(values, AppSettingKeys.MonitoringSearchEnabled),
            GetInt(values, AppSettingKeys.MonitoringIntervalMinutes));
    }

    /// <summary>Returns the acquisition recycle-bin settings (a blank path disables the bin).</summary>
    public async Task<RecycleBinSettings> GetRecycleBinSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([
            AppSettingKeys.AcquisitionRecycleBinPath,
            AppSettingKeys.AcquisitionRecycleBinCleanupDays,
        ], cancellationToken);

        var path = GetString(values, AppSettingKeys.AcquisitionRecycleBinPath);
        return new RecycleBinSettings(
            string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            GetInt(values, AppSettingKeys.AcquisitionRecycleBinCleanupDays));
    }

    /// <summary>
    /// Returns the proper/repack download policy. Decodes the stored
    /// <see cref="ProperDownloadPolicy"/> code, falling back to
    /// <see cref="ProperDownloadPolicy.PreferAndUpgrade"/> when the value is missing or unknown.
    /// </summary>
    public async Task<ProperDownloadSettings> GetProperDownloadSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([AppSettingKeys.AcquisitionDownloadPropers], cancellationToken);
        var code = GetString(values, AppSettingKeys.AcquisitionDownloadPropers);
        var policy = code.TryDecodeAs<ProperDownloadPolicy>(out var decoded) ? decoded : ProperDownloadPolicy.PreferAndUpgrade;
        return new ProperDownloadSettings(policy);
    }

    /// <summary>
    /// Returns recurring collection refresh settings.
    /// </summary>
    public async Task<CollectionRefreshSettings> GetCollectionRefreshSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([AppSettingKeys.CollectionsAutoRefreshEnabled], cancellationToken);
        return new CollectionRefreshSettings(GetBoolean(values, AppSettingKeys.CollectionsAutoRefreshEnabled));
    }

    /// <summary>
    /// Returns auto-identify settings used to drive plugin identification during scans.
    /// The stored confidence threshold is a 0–100 percentage and is returned here as a 0–1 fraction.
    /// </summary>
    public async Task<AutoIdentifySettings> GetAutoIdentifySettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([
            AppSettingKeys.AutoIdentifyEnabled,
            AppSettingKeys.AutoIdentifyProviders,
            AppSettingKeys.AutoIdentifyEntityKinds,
            AppSettingKeys.AutoIdentifyConfidenceThreshold,
            AppSettingKeys.AutoIdentifyUnorganizedOnly,
        ], cancellationToken);

        var percent = GetFloat(values, AppSettingKeys.AutoIdentifyConfidenceThreshold);
        return new AutoIdentifySettings(
            GetBoolean(values, AppSettingKeys.AutoIdentifyEnabled),
            GetStringList(values, AppSettingKeys.AutoIdentifyProviders),
            GetStringList(values, AppSettingKeys.AutoIdentifyEntityKinds),
            Math.Clamp(percent / 100d, 0d, 1d),
            GetBoolean(values, AppSettingKeys.AutoIdentifyUnorganizedOnly));
    }

    /// <summary>
    /// Returns whether library scans should delete tags that nothing references.
    /// </summary>
    public async Task<bool> GetRemoveOrphanTagsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([AppSettingKeys.TaxonomyRemoveOrphanTags], cancellationToken);
        return GetBoolean(values, AppSettingKeys.TaxonomyRemoveOrphanTags);
    }

    /// <summary>
    /// Returns generation-pipeline settings used by scan and maintenance jobs.
    /// </summary>
    public async Task<GenerationSettings> GetGenerationSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([
            AppSettingKeys.GenerationAutoGenerateMetadata,
            AppSettingKeys.GenerationAutoGenerateOshash,
            AppSettingKeys.GenerationAutoGenerateMd5,
            AppSettingKeys.GenerationAutoGeneratePreview,
            AppSettingKeys.GenerationGenerateTrickplay,
            AppSettingKeys.GenerationTrickplayIntervalSeconds,
            AppSettingKeys.GenerationPreviewClipDurationSeconds,
            AppSettingKeys.GenerationThumbnailQuality,
            AppSettingKeys.GenerationTrickplayQuality,
            AppSettingKeys.GenerationMetadataStorageDedicated,
        ], cancellationToken);

        return new GenerationSettings(
            GetBoolean(values, AppSettingKeys.GenerationAutoGenerateMetadata),
            GetBoolean(values, AppSettingKeys.GenerationAutoGenerateOshash),
            GetBoolean(values, AppSettingKeys.GenerationAutoGenerateMd5),
            GetBoolean(values, AppSettingKeys.GenerationAutoGeneratePreview),
            GetBoolean(values, AppSettingKeys.GenerationGenerateTrickplay),
            GetInt(values, AppSettingKeys.GenerationTrickplayIntervalSeconds),
            GetInt(values, AppSettingKeys.GenerationPreviewClipDurationSeconds),
            GetSelectInt(values, AppSettingKeys.GenerationThumbnailQuality, 2),
            GetSelectInt(values, AppSettingKeys.GenerationTrickplayQuality, 2),
            GetBoolean(values, AppSettingKeys.GenerationMetadataStorageDedicated));
    }

    /// <summary>
    /// Returns worker throughput settings.
    /// </summary>
    public async Task<WorkerSettings> GetWorkerSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([AppSettingKeys.JobsBackgroundConcurrency], cancellationToken);
        return new WorkerSettings(GetInt(values, AppSettingKeys.JobsBackgroundConcurrency));
    }

    /// <summary>
    /// Returns playback defaults.
    /// </summary>
    public async Task<PlaybackSettings> GetPlaybackSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([
            AppSettingKeys.PlaybackDefaultMode,
            AppSettingKeys.PlaybackShowCastControls,
            AppSettingKeys.PlaybackAudioPreferredLanguages,
        ], cancellationToken);

        return new PlaybackSettings(
            GetString(values, AppSettingKeys.PlaybackDefaultMode),
            GetBoolean(values, AppSettingKeys.PlaybackShowCastControls),
            GetStringList(values, AppSettingKeys.PlaybackAudioPreferredLanguages));
    }

    /// <summary>
    /// Returns subtitle behavior and appearance defaults.
    /// </summary>
    public async Task<SubtitleSettings> GetSubtitleSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([
            AppSettingKeys.SubtitlesAutoEnable,
            AppSettingKeys.SubtitlesPreferredLanguages,
            AppSettingKeys.SubtitlesStyle,
            AppSettingKeys.SubtitlesFontScale,
            AppSettingKeys.SubtitlesPositionPercent,
            AppSettingKeys.SubtitlesOpacity,
        ], cancellationToken);

        return new SubtitleSettings(
            GetBoolean(values, AppSettingKeys.SubtitlesAutoEnable),
            GetStringList(values, AppSettingKeys.SubtitlesPreferredLanguages),
            GetString(values, AppSettingKeys.SubtitlesStyle),
            GetFloat(values, AppSettingKeys.SubtitlesFontScale),
            GetFloat(values, AppSettingKeys.SubtitlesPositionPercent),
            GetFloat(values, AppSettingKeys.SubtitlesOpacity));
    }

    /// <summary>
    /// Returns HLS transcoder and ffmpeg settings.
    /// </summary>
    public async Task<HlsSettings> GetHlsSettingsAsync(CancellationToken cancellationToken) {
        var values = await GetValueMapAsync([
            AppSettingKeys.HlsTranscoderProfile,
            AppSettingKeys.HlsFfmpegPath,
            AppSettingKeys.HlsVaapiDevice,
            AppSettingKeys.HlsEnableAdaptiveBitrate,
            AppSettingKeys.HlsEncodingThreadCount,
            AppSettingKeys.HlsMaxCacheSizeGb,
        ], cancellationToken);

        return new HlsSettings(
            GetString(values, AppSettingKeys.HlsTranscoderProfile),
            GetString(values, AppSettingKeys.HlsFfmpegPath),
            GetString(values, AppSettingKeys.HlsVaapiDevice),
            GetBoolean(values, AppSettingKeys.HlsEnableAdaptiveBitrate),
            GetInt(values, AppSettingKeys.HlsEncodingThreadCount),
            GetInt(values, AppSettingKeys.HlsMaxCacheSizeGb));
    }

    private async Task<IReadOnlyDictionary<string, JsonElement>> GetValueMapAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken) =>
        (await GetValuesAsync(keys, cancellationToken)).Values;

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

    private static bool GetBoolean(IReadOnlyDictionary<string, JsonElement> values, string key) => values[key].GetBoolean();

    private static int GetInt(IReadOnlyDictionary<string, JsonElement> values, string key) => values[key].GetInt32();

    /// <summary>
    /// Reads a Select-type setting whose option values are numeric strings.
    /// </summary>
    private static int GetSelectInt(IReadOnlyDictionary<string, JsonElement> values, string key, int fallback) {
        var element = values[key];
        if (element.ValueKind == JsonValueKind.Number)
            return element.GetInt32();
        var raw = element.GetString();
        return int.TryParse(raw, out var result) ? result : fallback;
    }

    private static float GetFloat(IReadOnlyDictionary<string, JsonElement> values, string key) =>
        (float)values[key].GetDouble();

    private static string GetString(IReadOnlyDictionary<string, JsonElement> values, string key) =>
        values[key].GetString() ?? string.Empty;

    private static IReadOnlyList<string> GetStringList(IReadOnlyDictionary<string, JsonElement> values, string key) =>
        values[key].EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
}
