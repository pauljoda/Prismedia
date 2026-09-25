import type { LedStatus } from "@prismedia/ui-svelte";
import { CONNECTION_STATUS, DATABASE_BACKUP_STATUS, USER_ROLE } from "$lib/api/generated/codes";
import type {
  ConnectionResponse,
  DownloadClientSummary,
  IndexerConfigSummary,
  UserResponse,
} from "$lib/api/generated/model";
import type {
  DatabaseBackupList,
  LibraryRoot,
  SettingDescriptor,
  SettingsCatalogResponse,
  TranscodeCacheStatus,
} from "$lib/api/settings";
import {
  findSetting,
  settingKeys,
  valueAsBoolean,
  valueAsNumber,
  valueAsString,
  valueAsStringList,
  type SettingKey,
} from "$lib/settings/app-settings";
import { SETTING_SECTION, type SettingsSectionId } from "$lib/settings/settings-section-catalog";
import { formatBytes, formatRelativeTime } from "$lib/utils/format";
import { latestRootScan } from "./library-roots";
import { formatEvery, formatUntil, plural } from "./operate-status";

/** View families for the Settings index. Presentation grouping only; routes stay as they are. */
export const SETTINGS_FAMILY = {
  library: "library",
  acquisition: "acquisition",
  playback: "playback",
  system: "system",
} as const;

export type SettingsFamilyId = (typeof SETTINGS_FAMILY)[keyof typeof SETTINGS_FAMILY];

export interface SettingsFamily {
  id: SettingsFamilyId;
  title: string;
  sections: readonly SettingsSectionId[];
}

export const SETTINGS_FAMILIES: readonly SettingsFamily[] = [
  {
    id: SETTINGS_FAMILY.library,
    title: "Library",
    sections: [SETTING_SECTION.libraries, SETTING_SECTION.generation, SETTING_SECTION.autoIdentify],
  },
  {
    id: SETTINGS_FAMILY.acquisition,
    title: "Acquisition",
    sections: [SETTING_SECTION.acquisition, SETTING_SECTION.connections],
  },
  {
    id: SETTINGS_FAMILY.playback,
    title: "Playback",
    sections: [SETTING_SECTION.playback, SETTING_SECTION.subtitles, SETTING_SECTION.transcodeCache],
  },
  {
    id: SETTINGS_FAMILY.system,
    title: "System",
    sections: [SETTING_SECTION.users, SETTING_SECTION.databaseBackups, SETTING_SECTION.diagnostics],
  },
];

/**
 * Where each server setting is edited, so filtering by a setting's name finds the card that owns it.
 * Exhaustive over the generated key set: a new setting must be placed before this compiles.
 */
const SECTION_BY_SETTING = {
  [settingKeys.acquisitionDownloadPropers]: SETTING_SECTION.acquisition,
  [settingKeys.acquisitionPreferredProtocol]: SETTING_SECTION.acquisition,
  [settingKeys.acquisitionRecycleBinCleanupDays]: SETTING_SECTION.acquisition,
  [settingKeys.acquisitionRecycleBinPath]: SETTING_SECTION.acquisition,
  [settingKeys.monitoringIntervalMinutes]: SETTING_SECTION.acquisition,
  [settingKeys.monitoringSearchEnabled]: SETTING_SECTION.acquisition,
  [settingKeys.autoIdentifyConfidenceThreshold]: SETTING_SECTION.autoIdentify,
  [settingKeys.autoIdentifyEnabled]: SETTING_SECTION.autoIdentify,
  [settingKeys.autoIdentifyEntityKinds]: SETTING_SECTION.autoIdentify,
  [settingKeys.autoIdentifyProviders]: SETTING_SECTION.autoIdentify,
  [settingKeys.autoIdentifyUnorganizedOnly]: SETTING_SECTION.autoIdentify,
  [settingKeys.identifyDefaultProviders]: SETTING_SECTION.autoIdentify,
  [settingKeys.collectionsAutoRefreshEnabled]: SETTING_SECTION.generation,
  [settingKeys.generationAutoGenerateMd5]: SETTING_SECTION.generation,
  [settingKeys.generationAutoGenerateMetadata]: SETTING_SECTION.generation,
  [settingKeys.generationAutoGenerateOshash]: SETTING_SECTION.generation,
  [settingKeys.generationAutoGeneratePreview]: SETTING_SECTION.generation,
  [settingKeys.generationGenerateTrickplay]: SETTING_SECTION.generation,
  [settingKeys.generationMetadataStorageDedicated]: SETTING_SECTION.generation,
  [settingKeys.generationPreviewClipDurationSeconds]: SETTING_SECTION.generation,
  [settingKeys.generationThumbnailQuality]: SETTING_SECTION.generation,
  [settingKeys.generationTrickplayIntervalSeconds]: SETTING_SECTION.generation,
  [settingKeys.generationTrickplayQuality]: SETTING_SECTION.generation,
  [settingKeys.jobsBackgroundConcurrency]: SETTING_SECTION.generation,
  [settingKeys.pluginsAutoUpdateEnabled]: SETTING_SECTION.generation,
  [settingKeys.scanAutoScanEnabled]: SETTING_SECTION.generation,
  [settingKeys.scanIntegrityIntervalHours]: SETTING_SECTION.generation,
  [settingKeys.scanIntervalMinutes]: SETTING_SECTION.generation,
  [settingKeys.taxonomyRemoveOrphanTags]: SETTING_SECTION.generation,
  [settingKeys.hlsEnableAdaptiveBitrate]: SETTING_SECTION.playback,
  [settingKeys.hlsEncodingThreadCount]: SETTING_SECTION.playback,
  [settingKeys.hlsFfmpegPath]: SETTING_SECTION.playback,
  [settingKeys.hlsTranscoderProfile]: SETTING_SECTION.playback,
  [settingKeys.hlsVaapiDevice]: SETTING_SECTION.playback,
  [settingKeys.playbackAudioPreferredLanguages]: SETTING_SECTION.playback,
  [settingKeys.playbackDefaultMode]: SETTING_SECTION.playback,
  [settingKeys.playbackShowCastControls]: SETTING_SECTION.playback,
  [settingKeys.hlsMaxCacheSizeGb]: SETTING_SECTION.transcodeCache,
  [settingKeys.subtitlesAutoDownloadEnabled]: SETTING_SECTION.subtitles,
  [settingKeys.subtitlesAutoDownloadLanguages]: SETTING_SECTION.subtitles,
  [settingKeys.subtitlesAutoDownloadMinimumConfidence]: SETTING_SECTION.subtitles,
  [settingKeys.subtitlesAutoEnable]: SETTING_SECTION.subtitles,
  [settingKeys.subtitlesFontScale]: SETTING_SECTION.subtitles,
  [settingKeys.subtitlesOpacity]: SETTING_SECTION.subtitles,
  [settingKeys.subtitlesPositionPercent]: SETTING_SECTION.subtitles,
  [settingKeys.subtitlesPreferredLanguages]: SETTING_SECTION.subtitles,
  [settingKeys.subtitlesStyle]: SETTING_SECTION.subtitles,
  [settingKeys.visibilityDefaultMode]: SETTING_SECTION.users,
} as const satisfies Record<SettingKey, SettingsSectionId>;

/** Every catalog setting grouped under the section that edits it. */
export function settingsBySection(catalog: SettingsCatalogResponse | null): Map<SettingsSectionId, SettingDescriptor[]> {
  const grouped = new Map<SettingsSectionId, SettingDescriptor[]>();
  for (const group of catalog?.groups ?? []) {
    for (const setting of group.settings) {
      const section = (SECTION_BY_SETTING as Record<string, SettingsSectionId>)[setting.key];
      if (!section) continue;
      const list = grouped.get(section);
      if (list) list.push(setting);
      else grouped.set(section, [setting]);
    }
  }
  return grouped;
}

/** What a settings card says is configured right now. */
export interface SectionStatus {
  led: LedStatus;
  text: string;
  /** Optional fill for capacity-style sections, 0–100. */
  fill?: number;
}

// ── Live status from section-specific endpoints ───────────────

export function librariesStatus(roots: readonly LibraryRoot[]): SectionStatus {
  if (roots.length === 0) return { led: "warning", text: "0 libraries" };
  const last = latestRootScan(roots);
  const unscanned = roots.filter((root) => root.enabled && !root.lastScannedAt).length;
  return {
    led: unscanned > 0 ? "warning" : "phosphor",
    text: [plural(roots.length, "library", "libraries"), last ? `scan ${formatRelativeTime(last)}` : "never scanned"]
      .concat(unscanned > 0 ? [`${unscanned} unscanned`] : [])
      .join(" · "),
  };
}

export function usersStatus(users: readonly UserResponse[]): SectionStatus {
  const admins = users.filter((user) => user.role === USER_ROLE.admin).length;
  const disabled = users.filter((user) => !user.enabled).length;
  return {
    led: "phosphor",
    text: [plural(users.length, "account"), plural(admins, "admin"), disabled > 0 ? `${disabled} disabled` : null]
      .filter(Boolean)
      .join(" · "),
  };
}

export function acquisitionStatus(
  indexers: readonly IndexerConfigSummary[],
  clients: readonly DownloadClientSummary[],
): SectionStatus {
  const enabledIndexers = indexers.filter((indexer) => indexer.enabled);
  const enabledClients = clients.filter((client) => client.enabled);
  const failing = enabledIndexers.filter(
    (indexer) => indexer.lastFailureMessage || (indexer.disabledUntil && Date.parse(indexer.disabledUntil) > Date.now()),
  ).length;
  if (enabledIndexers.length === 0 && enabledClients.length === 0) return { led: "idle", text: "0 indexers · 0 clients" };
  return {
    led: failing > 0 ? "warning" : "phosphor",
    text: [plural(enabledIndexers.length, "indexer"), plural(enabledClients.length, "client")]
      .concat(failing > 0 ? [`${failing} failing`] : [])
      .join(" · "),
  };
}

export function connectionsStatus(connections: readonly ConnectionResponse[]): SectionStatus {
  const enabled = connections.filter((connection) => connection.enabled);
  if (enabled.length === 0) return { led: "idle", text: "0 connections" };
  const ready = enabled.filter((connection) => connection.status === CONNECTION_STATUS.ready).length;
  const broken = enabled.filter(
    (connection) => connection.status === CONNECTION_STATUS.unavailable || connection.status === CONNECTION_STATUS.identityChanged,
  ).length;
  return {
    led: broken > 0 ? "error" : ready === enabled.length ? "phosphor" : "warning",
    text: `${ready}/${enabled.length} ready`,
  };
}

export function transcodeCacheStatus(cache: TranscodeCacheStatus): SectionStatus {
  const fill = cache.maxBytes > 0 ? Math.min(100, (cache.usedBytes / cache.maxBytes) * 100) : 0;
  return {
    led: fill >= 90 ? "warning" : "phosphor",
    text: `${formatBytes(cache.usedBytes)} / ${formatBytes(cache.maxBytes)}`,
    fill,
  };
}

export function backupsStatus(list: DatabaseBackupList): SectionStatus {
  const latest = list.backups[0];
  const lastGood = list.backups.find((backup) => backup.status === DATABASE_BACKUP_STATUS.completed);
  const next = formatUntil(list.nextAutomaticBackupAt);
  if (!lastGood) return { led: "warning", text: next ? `0 backups · next ${next}` : "0 backups" };
  const failed = latest?.status === DATABASE_BACKUP_STATUS.failed;
  return {
    led: failed ? "error" : "phosphor",
    text: [
      failed ? "last failed" : `last ${formatRelativeTime(lastGood.completedAt ?? lastGood.createdAt)}`,
      next ? `next ${next}` : null,
    ]
      .filter(Boolean)
      .join(" · "),
  };
}

// ── Status read straight from the settings catalog ────────────

function optionLabel(catalog: SettingsCatalogResponse, key: SettingKey): string | null {
  const setting = findSetting(catalog, key);
  if (!setting) return null;
  const value = valueAsString(setting.value);
  return setting.options?.find((option) => option.value === value)?.label ?? (value || null);
}

export function playbackStatus(catalog: SettingsCatalogResponse): SectionStatus {
  const mode = optionLabel(catalog, settingKeys.playbackDefaultMode);
  const transcoder = optionLabel(catalog, settingKeys.hlsTranscoderProfile);
  return {
    led: "phosphor",
    text: [mode ? `${mode} default` : null, transcoder ? `${transcoder} transcoder` : null].filter(Boolean).join(" · "),
  };
}

export function subtitlesStatus(catalog: SettingsCatalogResponse): SectionStatus {
  const autoEnable = valueAsBoolean(findSetting(catalog, settingKeys.subtitlesAutoEnable)?.value);
  const autoDownload = valueAsBoolean(findSetting(catalog, settingKeys.subtitlesAutoDownloadEnabled)?.value);
  const languages = valueAsStringList(findSetting(catalog, settingKeys.subtitlesAutoDownloadLanguages)?.value);
  return {
    led: autoEnable || autoDownload ? "phosphor" : "idle",
    text: [
      autoEnable ? "captions on" : "captions off",
      autoDownload ? `auto-download ${languages.join(", ") || "on"}` : "auto-download off",
    ].join(" · "),
  };
}

export function generationStatus(catalog: SettingsCatalogResponse): SectionStatus {
  const autoScan = valueAsBoolean(findSetting(catalog, settingKeys.scanAutoScanEnabled)?.value);
  const interval = valueAsNumber(findSetting(catalog, settingKeys.scanIntervalMinutes)?.value, 0);
  const workers = valueAsNumber(findSetting(catalog, settingKeys.jobsBackgroundConcurrency)?.value, 0);
  const trickplay = valueAsBoolean(findSetting(catalog, settingKeys.generationGenerateTrickplay)?.value);
  return {
    led: autoScan ? "phosphor" : "idle",
    text: [
      autoScan ? `auto-scan ${formatEvery(interval)}` : "auto-scan off",
      workers > 0 ? plural(workers, "worker") : null,
      trickplay ? "trickplay on" : null,
    ]
      .filter(Boolean)
      .join(" · "),
  };
}

export function autoIdentifyStatus(catalog: SettingsCatalogResponse): SectionStatus {
  const enabled = valueAsBoolean(findSetting(catalog, settingKeys.autoIdentifyEnabled)?.value);
  const providers = valueAsStringList(findSetting(catalog, settingKeys.autoIdentifyProviders)?.value);
  const threshold = valueAsNumber(findSetting(catalog, settingKeys.autoIdentifyConfidenceThreshold)?.value, 0);
  if (!enabled) return { led: "idle", text: "auto off" };
  return {
    led: providers.length > 0 ? "phosphor" : "warning",
    text: ["auto on", plural(providers.length, "provider"), threshold > 0 ? `≥${threshold}%` : null]
      .filter(Boolean)
      .join(" · "),
  };
}

export const DIAGNOSTICS_STATUS: SectionStatus = { led: "idle", text: "on demand" };
