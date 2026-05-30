import type {
  LibrarySettings,
  SettingDescriptor,
  SettingsCatalogResponse,
  SettingsGroup,
  SettingValue,
} from "$lib/api/settings";

export const settingKeys = {
  visibilityDefaultMode: "visibility.defaultMode",
  visibilityLanAutoEnable: "visibility.lanAutoEnable",
  scanAutoScanEnabled: "scan.autoScanEnabled",
  scanIntervalMinutes: "scan.intervalMinutes",
  autoIdentifyEnabled: "autoIdentify.enabled",
  autoIdentifyProviders: "autoIdentify.providers",
  autoIdentifyEntityKinds: "autoIdentify.entityKinds",
  autoIdentifyConfidenceThreshold: "autoIdentify.confidenceThreshold",
  autoIdentifyUnorganizedOnly: "autoIdentify.unorganizedOnly",
  generationAutoGenerateMetadata: "generation.autoGenerateMetadata",
  generationAutoGenerateOshash: "generation.autoGenerateOshash",
  generationAutoGenerateMd5: "generation.autoGenerateMd5",
  generationGeneratePhash: "generation.generatePhash",
  generationAutoGeneratePreview: "generation.autoGeneratePreview",
  generationGenerateTrickplay: "generation.generateTrickplay",
  generationMetadataStorageDedicated: "generation.metadataStorageDedicated",
  generationTrickplayIntervalSeconds: "generation.trickplayIntervalSeconds",
  generationPreviewClipDurationSeconds: "generation.previewClipDurationSeconds",
  generationThumbnailQuality: "generation.thumbnailQuality",
  generationTrickplayQuality: "generation.trickplayQuality",
  jobsBackgroundConcurrency: "jobs.backgroundConcurrency",
  playbackDefaultMode: "playback.defaultMode",
  playbackShowCastControls: "playback.showCastControls",
  playbackAudioPreferredLanguages: "playback.audioPreferredLanguages",
  subtitlesAutoEnable: "subtitles.autoEnable",
  subtitlesPreferredLanguages: "subtitles.preferredLanguages",
  subtitlesStyle: "subtitles.style",
  subtitlesFontScale: "subtitles.fontScale",
  subtitlesPositionPercent: "subtitles.positionPercent",
  subtitlesOpacity: "subtitles.opacity",
  hlsTranscoderProfile: "hls.transcoderProfile",
  hlsFfmpegPath: "hls.ffmpegPath",
  hlsVaapiDevice: "hls.vaapiDevice",
} as const;

export type SettingKey = (typeof settingKeys)[keyof typeof settingKeys];

export const defaultLibrarySettings: LibrarySettings = {
  visibilityDefaultMode: "off",
  nsfwLanAutoEnable: false,
  autoScanEnabled: false,
  scanIntervalMinutes: 60,
  autoIdentifyEnabled: false,
  autoIdentifyProviders: [],
  autoIdentifyEntityKinds: ["video", "gallery", "image", "audio", "book"],
  autoIdentifyConfidenceThreshold: 90,
  autoIdentifyUnorganizedOnly: true,
  autoGenerateMetadata: true,
  autoGenerateOshash: true,
  autoGenerateMd5: false,
  generatePhash: false,
  autoGeneratePreview: true,
  generateTrickplay: true,
  metadataStorageDedicated: true,
  trickplayIntervalSeconds: 10,
  previewClipDurationSeconds: 8,
  thumbnailQuality: "2",
  trickplayQuality: "2",
  backgroundWorkerConcurrency: 1,
  defaultPlaybackMode: "direct",
  showCastControls: true,
  audioPreferredLanguages: "en,eng,en-US",
  subtitlesAutoEnable: false,
  subtitlesPreferredLanguages: "en,eng",
  subtitleStyle: "stylized",
  subtitleFontScale: 1,
  subtitlePositionPercent: 88,
  subtitleOpacity: 1,
  hlsTranscoderProfile: "Auto",
  hlsFfmpegPath: "ffmpeg",
  hlsVaapiDevice: "/dev/dri/renderD128",
};

export function settingsInGroup(
  catalog: SettingsCatalogResponse | null,
  groupKey: string,
  excludedKeys: readonly string[] = [],
): SettingDescriptor[] {
  const excluded = new Set(excludedKeys);
  return catalog?.groups
    .find((group) => group.key === groupKey)
    ?.settings.filter((setting) => !excluded.has(setting.key)) ?? [];
}

export function findSettingsGroup(
  catalog: SettingsCatalogResponse | null,
  groupKey: string,
): SettingsGroup | null {
  return catalog?.groups.find((group) => group.key === groupKey) ?? null;
}

export function findSetting(
  catalog: SettingsCatalogResponse | null,
  key: string,
): SettingDescriptor | null {
  for (const group of catalog?.groups ?? []) {
    const setting = group.settings.find((candidate) => candidate.key === key);
    if (setting) return setting;
  }
  return null;
}

export function replaceSetting(
  catalog: SettingsCatalogResponse | null,
  updated: SettingDescriptor,
): SettingsCatalogResponse | null {
  if (!catalog) return catalog;
  return {
    groups: catalog.groups.map((group) => ({
      ...group,
      settings: group.settings.map((setting) =>
        setting.key === updated.key ? updated : setting,
      ),
    })),
  };
}

export function valueAsBoolean(value: SettingValue | undefined, fallback = false): boolean {
  return typeof value === "boolean" ? value : fallback;
}

export function valueAsNumber(value: SettingValue | undefined, fallback: number): number {
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

export function valueAsString(value: SettingValue | undefined, fallback = ""): string {
  return typeof value === "string" ? value : fallback;
}

export function valueAsStringListText(value: SettingValue | undefined, fallback = ""): string {
  if (Array.isArray(value)) return value.join(",");
  if (typeof value === "string") return value;
  return fallback;
}

export function valueAsStringList(value: SettingValue | undefined, fallback: string[] = []): string[] {
  if (Array.isArray(value)) return value.map((item) => String(item)).filter(Boolean);
  if (typeof value === "string" && value.trim()) return parseStringList(value);
  return fallback;
}

export function parseStringList(text: string): string[] {
  return text
    .split(",")
    .map((part) => part.trim())
    .filter(Boolean);
}

export function catalogToLibrarySettings(
  catalog: SettingsCatalogResponse | null,
): LibrarySettings {
  const values: Record<string, SettingValue> = {};
  for (const group of catalog?.groups ?? []) {
    for (const setting of group.settings) {
      values[setting.key] = setting.value;
    }
  }
  return valuesToLibrarySettings(values);
}

export function valuesToLibrarySettings(
  values: Record<string, SettingValue>,
): LibrarySettings {
  const fallback = defaultLibrarySettings;
  const visibilityDefaultMode = valueAsString(
    values[settingKeys.visibilityDefaultMode],
    fallback.visibilityDefaultMode,
  );
  const playbackDefaultMode = valueAsString(
    values[settingKeys.playbackDefaultMode],
    fallback.defaultPlaybackMode,
  );

  return {
    visibilityDefaultMode: visibilityDefaultMode === "show" ? "show" : "off",
    nsfwLanAutoEnable: valueAsBoolean(
      values[settingKeys.visibilityLanAutoEnable],
      fallback.nsfwLanAutoEnable,
    ),
    autoScanEnabled: valueAsBoolean(
      values[settingKeys.scanAutoScanEnabled],
      fallback.autoScanEnabled,
    ),
    scanIntervalMinutes: valueAsNumber(
      values[settingKeys.scanIntervalMinutes],
      fallback.scanIntervalMinutes,
    ),
    autoIdentifyEnabled: valueAsBoolean(
      values[settingKeys.autoIdentifyEnabled],
      fallback.autoIdentifyEnabled,
    ),
    autoIdentifyProviders: valueAsStringList(
      values[settingKeys.autoIdentifyProviders],
      fallback.autoIdentifyProviders,
    ),
    autoIdentifyEntityKinds: valueAsStringList(
      values[settingKeys.autoIdentifyEntityKinds],
      fallback.autoIdentifyEntityKinds,
    ),
    autoIdentifyConfidenceThreshold: valueAsNumber(
      values[settingKeys.autoIdentifyConfidenceThreshold],
      fallback.autoIdentifyConfidenceThreshold,
    ),
    autoIdentifyUnorganizedOnly: valueAsBoolean(
      values[settingKeys.autoIdentifyUnorganizedOnly],
      fallback.autoIdentifyUnorganizedOnly,
    ),
    autoGenerateMetadata: valueAsBoolean(
      values[settingKeys.generationAutoGenerateMetadata],
      fallback.autoGenerateMetadata,
    ),
    autoGenerateOshash: valueAsBoolean(
      values[settingKeys.generationAutoGenerateOshash],
      fallback.autoGenerateOshash,
    ),
    autoGenerateMd5: valueAsBoolean(
      values[settingKeys.generationAutoGenerateMd5],
      fallback.autoGenerateMd5,
    ),
    generatePhash: valueAsBoolean(
      values[settingKeys.generationGeneratePhash],
      fallback.generatePhash,
    ),
    autoGeneratePreview: valueAsBoolean(
      values[settingKeys.generationAutoGeneratePreview],
      fallback.autoGeneratePreview,
    ),
    generateTrickplay: valueAsBoolean(
      values[settingKeys.generationGenerateTrickplay],
      fallback.generateTrickplay,
    ),
    metadataStorageDedicated: valueAsBoolean(
      values[settingKeys.generationMetadataStorageDedicated],
      fallback.metadataStorageDedicated,
    ),
    trickplayIntervalSeconds: valueAsNumber(
      values[settingKeys.generationTrickplayIntervalSeconds],
      fallback.trickplayIntervalSeconds,
    ),
    previewClipDurationSeconds: valueAsNumber(
      values[settingKeys.generationPreviewClipDurationSeconds],
      fallback.previewClipDurationSeconds,
    ),
    thumbnailQuality: valueAsString(
      values[settingKeys.generationThumbnailQuality],
      fallback.thumbnailQuality,
    ),
    trickplayQuality: valueAsString(
      values[settingKeys.generationTrickplayQuality],
      fallback.trickplayQuality,
    ),
    backgroundWorkerConcurrency: valueAsNumber(
      values[settingKeys.jobsBackgroundConcurrency],
      fallback.backgroundWorkerConcurrency,
    ),
    defaultPlaybackMode: playbackDefaultMode === "hls" ? "hls" : "direct",
    showCastControls: valueAsBoolean(
      values[settingKeys.playbackShowCastControls],
      fallback.showCastControls,
    ),
    audioPreferredLanguages: valueAsStringListText(
      values[settingKeys.playbackAudioPreferredLanguages],
      fallback.audioPreferredLanguages,
    ),
    subtitlesAutoEnable: valueAsBoolean(
      values[settingKeys.subtitlesAutoEnable],
      fallback.subtitlesAutoEnable,
    ),
    subtitlesPreferredLanguages: valueAsStringListText(
      values[settingKeys.subtitlesPreferredLanguages],
      fallback.subtitlesPreferredLanguages,
    ),
    subtitleStyle: valueAsString(values[settingKeys.subtitlesStyle], fallback.subtitleStyle),
    subtitleFontScale: valueAsNumber(
      values[settingKeys.subtitlesFontScale],
      fallback.subtitleFontScale,
    ),
    subtitlePositionPercent: valueAsNumber(
      values[settingKeys.subtitlesPositionPercent],
      fallback.subtitlePositionPercent,
    ),
    subtitleOpacity: valueAsNumber(
      values[settingKeys.subtitlesOpacity],
      fallback.subtitleOpacity,
    ),
    hlsTranscoderProfile: valueAsString(
      values[settingKeys.hlsTranscoderProfile],
      fallback.hlsTranscoderProfile,
    ),
    hlsFfmpegPath: valueAsString(values[settingKeys.hlsFfmpegPath], fallback.hlsFfmpegPath),
    hlsVaapiDevice: valueAsString(values[settingKeys.hlsVaapiDevice], fallback.hlsVaapiDevice),
  };
}
