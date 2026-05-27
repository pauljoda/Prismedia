/** Lightweight library root summary used by upload target pickers. */
export interface LibraryRootSummaryDto {
  id: string;
  path: string;
  label: string;
  enabled: boolean;
  scanVideos: boolean;
  scanImages: boolean;
  scanAudio: boolean;
  scanBooks: boolean;
}

export interface UploadVideoResponseDto {
  id: string;
  title: string;
  filePath: string;
  libraryRootId: string;
}

export interface UploadImageResponseDto {
  id: string;
  title: string;
  filePath: string;
  galleryId: string;
}

export interface UploadAudioTrackResponseDto {
  id: string;
  title: string;
  filePath: string;
  libraryId: string;
}

export interface LibraryRootDto {
  id: string;
  path: string;
  label: string;
  enabled: boolean;
  recursive: boolean;
  scanVideos: boolean;
  scanImages: boolean;
  scanAudio: boolean;
  scanBooks: boolean;
  isNsfw: boolean;
  lastScannedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface LibraryBrowseEntryDto {
  name: string;
  path: string;
}

export interface LibraryBrowseDto {
  path: string;
  parentPath: string | null;
  directories: LibraryBrowseEntryDto[];
}

export type AppSettingType =
  | "boolean"
  | "integer"
  | "decimal"
  | "string"
  | "stringList"
  | "select";

export type AppSettingValue = boolean | number | string | string[];

export interface AppSettingConstraintsDto {
  min?: number | null;
  max?: number | null;
  step?: number | null;
  minItems?: number | null;
  maxItems?: number | null;
}

export interface AppSettingOptionDto {
  value: string;
  label: string;
  description?: string | null;
}

export interface AppSettingDescriptorDto {
  key: string;
  groupKey: string;
  label: string;
  description: string;
  type: AppSettingType;
  value: AppSettingValue;
  defaultValue: AppSettingValue;
  isDefault: boolean;
  order: number;
  constraints: AppSettingConstraintsDto | null;
  options: AppSettingOptionDto[];
  inputKind?: string | null;
  applyHint?: string | null;
}

export interface AppSettingsGroupDto {
  key: string;
  label: string;
  description: string;
  order: number;
  settings: AppSettingDescriptorDto[];
}

export interface AppSettingsCatalogDto {
  groups: AppSettingsGroupDto[];
}

export interface AppSettingsValuesDto {
  values: Record<string, AppSettingValue>;
}
