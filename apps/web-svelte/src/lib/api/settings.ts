import {
  browseLibraryPath as browseLibraryPathRequest,
  createLibraryRoot as createLibraryRootRequest,
  deleteLibraryRoot as deleteLibraryRootRequest,
  getLibraryConfig,
  getSetting as getSettingRequest,
  getSettings,
  resetSetting as resetSettingRequest,
  updateLibraryRoot as updateLibraryRootRequest,
  updateSetting as updateSettingRequest,
  updateSettings as updateSettingsRequest,
} from "$lib/api/generated/prismedia";
import type {
  LibraryBrowseResponse as GeneratedLibraryBrowseResponse,
  LibraryConfigResponse as GeneratedLibraryConfigResponse,
  LibraryRoot as GeneratedLibraryRoot,
  SettingConstraints as GeneratedSettingConstraints,
  SettingDescriptor as GeneratedSettingDescriptor,
  SettingsCatalogResponse as GeneratedSettingsCatalogResponse,
  SettingsGroup as GeneratedSettingsGroup,
  SettingsValuesResponse as GeneratedSettingsValuesResponse,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";
import { fetchApi } from "$lib/api/orval-fetch";

export type SettingValue = boolean | number | string | string[];

export interface SettingConstraints {
  min?: number | null;
  max?: number | null;
  step?: number | null;
  minItems?: number | null;
  maxItems?: number | null;
}

export type SettingDescriptor = Omit<
  GeneratedSettingDescriptor,
  "value" | "defaultValue" | "order" | "constraints"
> & {
  value: SettingValue;
  defaultValue: SettingValue;
  order: number;
  constraints: SettingConstraints | null;
};

export type SettingsGroup = Omit<GeneratedSettingsGroup, "settings" | "order"> & {
  order: number;
  settings: SettingDescriptor[];
};

export interface SettingsCatalogResponse {
  groups: SettingsGroup[];
}

export type SettingsResponse = SettingsCatalogResponse;

export interface SettingsValuesResponse {
  values: Record<string, SettingValue>;
}

export interface LibrarySettings {
  visibilityDefaultMode: "off" | "show";
  nsfwLanAutoEnable: boolean;
  autoScanEnabled: boolean;
  scanIntervalMinutes: number;
  autoGenerateMetadata: boolean;
  autoGenerateFingerprints: boolean;
  generatePhash: boolean;
  autoGeneratePreview: boolean;
  generateTrickplay: boolean;
  metadataStorageDedicated: boolean;
  trickplayIntervalSeconds: number;
  previewClipDurationSeconds: number;
  thumbnailQuality: string;
  trickplayQuality: string;
  backgroundWorkerConcurrency: number;
  defaultPlaybackMode: "direct" | "hls";
  showCastControls: boolean;
  audioPreferredLanguages: string;
  subtitlesAutoEnable: boolean;
  subtitlesPreferredLanguages: string;
  subtitleStyle: string;
  subtitleFontScale: number;
  subtitlePositionPercent: number;
  subtitleOpacity: number;
  hlsTranscoderProfile: string;
  hlsFfmpegPath: string;
  hlsVaapiDevice: string;
}

export type LibraryRoot = GeneratedLibraryRoot;
export type LibraryBrowse = GeneratedLibraryBrowseResponse;

export interface LibraryConfigResponse {
  settings: SettingsCatalogResponse;
  roots: LibraryRoot[];
}

export async function fetchSettings(options?: RequestOptions): Promise<SettingsResponse> {
  const response = unwrapGenerated<GeneratedSettingsCatalogResponse>(await getSettings(requestInit(options)), "Failed to load settings");
  return normalizeSettingsCatalog(response);
}

export async function fetchSetting(
  key: string,
  options?: RequestOptions,
): Promise<SettingDescriptor> {
  const response = unwrapGenerated<GeneratedSettingDescriptor>(
    await getSettingRequest(key, requestInit(options)),
    "Failed to load setting",
  );
  return normalizeSettingDescriptor(response);
}

export async function fetchSettingsValues(
  keys: string[] = [],
  options?: RequestOptions,
): Promise<SettingsValuesResponse> {
  const search = new URLSearchParams();
  for (const key of keys) {
    search.append("keys", key);
  }

  const query = search.toString();
  const response = await fetchApi<GeneratedSettingsValuesResponse>(
    query ? `/settings/values?${query}` : "/settings/values",
    { signal: options?.signal },
  );
  return normalizeSettingsValues(response);
}

export async function updateSetting(
  key: string,
  value: SettingValue,
  options?: RequestOptions,
): Promise<SettingDescriptor> {
  const response = unwrapGenerated<GeneratedSettingDescriptor>(
    await updateSettingRequest(
      key,
      { value } as unknown as Parameters<typeof updateSettingRequest>[1],
      requestInit(options),
    ),
    "Failed to save setting",
  );
  return normalizeSettingDescriptor(response);
}

export async function updateSettings(
  values: Record<string, SettingValue>,
  options?: RequestOptions,
): Promise<SettingsCatalogResponse> {
  const response = unwrapGenerated<GeneratedSettingsCatalogResponse>(
    await updateSettingsRequest(
      { values } as unknown as Parameters<typeof updateSettingsRequest>[0],
      requestInit(options),
    ),
    "Failed to save settings",
  );
  return normalizeSettingsCatalog(response);
}

export async function resetSetting(
  key: string,
  options?: RequestOptions,
): Promise<SettingDescriptor> {
  const response = unwrapGenerated<GeneratedSettingDescriptor>(
    await resetSettingRequest(key, requestInit(options)),
    "Failed to reset setting",
  );
  return normalizeSettingDescriptor(response);
}

export async function fetchLibraryConfig(options?: RequestOptions): Promise<LibraryConfigResponse> {
  const response = unwrapGenerated<GeneratedLibraryConfigResponse>(
    await getLibraryConfig(requestInit(options)),
    "Failed to load settings",
  );
  return {
    settings: normalizeSettingsCatalog(response.settings),
    roots: response.roots,
  };
}

export async function browseLibraryPath(
  targetPath?: string,
  options?: RequestOptions,
): Promise<LibraryBrowse> {
  return unwrapGenerated(
    await browseLibraryPathRequest(targetPath ? { path: targetPath } : undefined, requestInit(options)),
    "Failed to browse folders",
  );
}

export async function createLibraryRoot(
  payload: Partial<LibraryRoot> & { path: string },
  options?: RequestOptions,
): Promise<LibraryRoot> {
  return unwrapGenerated(
    await createLibraryRootRequest(payload as unknown as Parameters<typeof createLibraryRootRequest>[0], requestInit(options)),
    "Failed to add library root",
  );
}

export async function updateLibraryRoot(
  id: string,
  payload: Partial<LibraryRoot>,
  options?: RequestOptions,
): Promise<LibraryRoot> {
  const response = await updateLibraryRootRequest(
    id,
    payload as unknown as Parameters<typeof updateLibraryRootRequest>[1],
    requestInit(options),
  );
  return unwrapGenerated(
    response,
    "Failed to update library root",
  );
}

export async function deleteLibraryRoot(
  id: string,
  options?: RequestOptions,
): Promise<{ ok: true }> {
  const response = await deleteLibraryRootRequest(id, requestInit(options));
  return unwrapGenerated(
    response,
    "Failed to remove library root",
  );
}

function normalizeSettingValue(value: unknown): SettingValue {
  if (Array.isArray(value)) {
    return value.map((item) => String(item));
  }
  if (typeof value === "boolean" || typeof value === "number" || typeof value === "string") {
    return value;
  }
  if (value == null) return "";
  return String(value);
}

function normalizeSettingDescriptor(descriptor: GeneratedSettingDescriptor): SettingDescriptor {
  return {
    ...descriptor,
    value: normalizeSettingValue(descriptor.value),
    defaultValue: normalizeSettingValue(descriptor.defaultValue),
    order: Number(descriptor.order),
    constraints: normalizeSettingConstraints(descriptor.constraints),
  };
}

function normalizeSettingConstraints(
  constraints: GeneratedSettingConstraints | null | undefined,
): SettingConstraints | null {
  if (!constraints) return null;
  return {
    min: normalizeOptionalNumber(constraints.min),
    max: normalizeOptionalNumber(constraints.max),
    step: normalizeOptionalNumber(constraints.step),
    minItems: normalizeOptionalNumber(constraints.minItems),
    maxItems: normalizeOptionalNumber(constraints.maxItems),
  };
}

function normalizeOptionalNumber(value: number | string | null | undefined): number | null {
  if (value == null || value === "") return null;
  return Number(value);
}

function normalizeSettingsCatalog(catalog: GeneratedSettingsCatalogResponse): SettingsCatalogResponse {
  return {
    groups: catalog.groups.map((group) => ({
      ...group,
      order: Number(group.order),
      settings: group.settings.map(normalizeSettingDescriptor),
    })),
  };
}

function normalizeSettingsValues(response: GeneratedSettingsValuesResponse): SettingsValuesResponse {
  return {
    values: Object.fromEntries(
      Object.entries(response.values ?? {}).map(([key, value]) => [
        key,
        normalizeSettingValue(value),
      ]),
    ),
  };
}
